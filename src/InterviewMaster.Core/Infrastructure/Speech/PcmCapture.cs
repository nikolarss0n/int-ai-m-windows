using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

/// Called on the source's capture thread with the buffer and the format it is in.
public delegate void PcmBufferHandler(ReadOnlySpan<byte> interleaved, PcmFormat format);

/// One opened audio device. The platform supplies it; a fresh one is opened for each start.
public interface IPcmSource
{
    /// Opens the device and starts delivering buffers, all from one thread. `onStopped` reports a
    /// device that ends on its own.
    Task StartAsync(PcmBufferHandler onBuffer, Action<Exception> onStopped);

    Task StopAsync();

    /// May be called from the capture thread, inside `onStopped`.
    void StopSync();
}

public sealed class AudioCapturePermissionException(CaptureDevice device) : Exception(
    device == CaptureDevice.Microphone ? PlatformCopy.MicrophonePermissionHelp : PlatformCopy.SystemAudioUnavailableHelp)
{
    public CaptureDevice Device { get; } = device;
}

/// The capture lifecycle `SystemAudioCapture` and `MicrophoneCapture` share on macOS: generations
/// reject a start that was stopped while it was still opening, and every buffer goes through the same
/// speech detector. Only opening the device is left to the platform.
public sealed class PcmCapture : AudioCaptureCallbacks, IAudioSourceCapture
{
    private readonly object gate = new();
    private readonly Func<IPcmSource> open;
    private readonly Func<CaptureDevice, bool, Task> preflight;
    private readonly SpeechTurnDetector.Labels labels;
    private readonly SpeechTurnDetector detector;
    private IPcmSource? source;
    private PcmConverter? converter;
    private ulong generation;
    private bool wantsCapture;
    private bool isCapturing;
    private bool isStopped;
    private bool muted;

    public PcmCapture(
        CaptureDevice device, IMainThread mainThread, Func<IPcmSource> open,
        Func<CaptureDevice, bool, Task> preflight, Func<DateTime>? now = null)
        : base(mainThread)
    {
        CaptureDevice = device;
        this.open = open;
        this.preflight = preflight;
        var isSystemAudio = device == CaptureDevice.SystemAudio;
        labels = isSystemAudio ? SpeechTurnDetector.Labels.SystemAudio : SpeechTurnDetector.Labels.Microphone;
        detector = new SpeechTurnDetector(
            labels,
            isSystemAudio ? SpeechTurnDetector.Tuning.SystemAudio : SpeechTurnDetector.Tuning.MicrophoneExperimental,
            isSystemAudio ? "SysVAD" : "MicVAD-UNMEASURED",
            DispatchTurn, DispatchLevel, DispatchStatus, now);
    }

    public CaptureDevice CaptureDevice { get; }

    public async Task StartCapturingAsync()
    {
        ulong request;
        lock (gate)
        {
            if (isStopped || wantsCapture) throw new OperationCanceledException();
            generation++;
            wantsCapture = true;
            request = generation;
        }
        IPcmSource? candidate = null;
        try
        {
            await preflight(CaptureDevice, false).ConfigureAwait(false);
            RequireCurrent(request);
            candidate = open();
            var opened = candidate;
            lock (gate)
            {
                if (!wantsCapture || generation != request) throw new OperationCanceledException();
                source = opened;
                converter = null;
            }
            await opened.StartAsync(
                (interleaved, format) => Consume(opened, request, interleaved, format),
                error => SourceStopped(opened, request, error)).ConfigureAwait(false);
            lock (gate)
            {
                if (!wantsCapture || generation != request || !ReferenceEquals(source, opened)) throw new OperationCanceledException();
                detector.Reset();
                detector.SetSampleRate(PcmConverter.OutputSampleRate);
                detector.SetMuted(muted);
                isCapturing = true;
                DispatchStatus(muted ? labels.Muted : labels.Idle);
            }
        }
        catch
        {
            // A stop during an awaited start cannot leave a successfully started orphan stream.
            if (candidate is not null)
            {
                try { await candidate.StopAsync().ConfigureAwait(false); }
                catch (Exception error) { Log.Print($"⚠️ Audio source did not stop cleanly: {error.Message}"); }
            }
            lock (gate)
            {
                if (generation == request)
                {
                    wantsCapture = false;
                    isCapturing = false;
                    source = null;
                    isStopped = true;
                    detector.Shutdown();
                }
            }
            throw;
        }
    }

    private void RequireCurrent(ulong request)
    {
        lock (gate)
        {
            if (!wantsCapture || generation != request) throw new OperationCanceledException();
        }
    }

    private IPcmSource? Detach()
    {
        lock (gate)
        {
            generation++;
            wantsCapture = false;
            isCapturing = false;
            isStopped = true;
            detector.Shutdown();
            var old = source;
            source = null;
            return old;
        }
    }

    public async Task StopCapturingAsync()
    {
        if (Detach() is not { } old) return;
        try { await old.StopAsync().ConfigureAwait(false); }
        catch (Exception error) { Log.Print($"⚠️ Audio source did not stop cleanly: {error.Message}"); }
    }

    public void StopCapturingSync()
    {
        try { Detach()?.StopSync(); }
        catch (Exception error) { Log.Print($"⚠️ Audio source did not stop cleanly: {error.Message}"); }
    }

    public void SetMuted(bool muted)
    {
        lock (gate)
        {
            this.muted = muted;
            detector.SetMuted(muted);
        }
    }

    private void Consume(IPcmSource from, ulong request, ReadOnlySpan<byte> interleaved, PcmFormat format)
    {
        Exception? failure = null;
        lock (gate)
        {
            if (!wantsCapture || !isCapturing || generation != request || !ReferenceEquals(source, from)) return;
            try
            {
                // A device that changes format mid-stream (a new default microphone) gets a new converter.
                if (converter is null || converter.Input != format)
                {
                    converter = new PcmConverter(format);
                    Log.Print($"AUDIO: {CaptureDevice.RawValue()} input {format.SampleRate} Hz/{format.Channels} channels {format.Kind}; output 48000 Hz/mono");
                }
                var samples = converter.Convert(interleaved);
                if (samples.Length > 0) detector.Ingest(samples);
            }
            catch (CaptureFormatException error)
            {
                failure = error;
            }
        }
        if (failure is not null) SourceStopped(from, request, failure);
    }

    private void SourceStopped(IPcmSource from, ulong request, Exception error)
    {
        lock (gate)
        {
            if (!wantsCapture || generation != request || !ReferenceEquals(source, from)) return;
            wantsCapture = false;
            isCapturing = false;
            if (CaptureDevice == CaptureDevice.SystemAudio)
            {
                DispatchStatus($"⚠️ System audio stopped: {error.Message}");
            }
            else
            {
                // The microphone path on macOS tears the engine down before it reports the failure.
                generation++;
                isStopped = true;
                source = null;
                detector.Shutdown();
                try { from.StopSync(); }
                catch (Exception stopError) { Log.Print($"⚠️ Audio source did not stop cleanly: {stopError.Message}"); }
                DispatchStatus($"Microphone unavailable: {error.Message}");
            }
            DispatchFailure(error);
        }
    }
}
