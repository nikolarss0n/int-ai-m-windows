using System.Diagnostics;
using System.Runtime.InteropServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Platform.Windows.Audio;

/// Reads the default microphone, or what the computer is playing, through WASAPI. Every COM object
/// is created, used and released on the one capture thread.
///
/// System audio prefers a process loopback that leaves out this app's own sound (practice speech),
/// as macOS does with `excludesCurrentProcessAudio`. Windows before build 20348 may refuse it; the
/// fallback is the default playback device's loopback, which includes this app's sound.
internal sealed class WasapiSource : IPcmSource
{
    private const int MaxReopenAttempts = 3;
    private static readonly TimeSpan JoinLimit = TimeSpan.FromSeconds(2);

    private readonly CaptureDevice device;
    private readonly ManualResetEvent stopSignal = new(false);
    private readonly AutoResetEvent dataSignal = new(false);
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? thread;
    private PcmBufferHandler? onBuffer;
    private Action<Exception>? onStopped;
    private volatile bool reopenRequested;

    private IMMDeviceEnumerator? enumerator;
    private DeviceChangeListener? listener;
    private IAudioClient? audioClient;
    private IAudioCaptureClient? captureClient;
    private PcmFormat format;
    private byte[] buffer = Array.Empty<byte>();

    public WasapiSource(CaptureDevice device)
    {
        this.device = device;
    }

    public Task StartAsync(PcmBufferHandler onBuffer, Action<Exception> onStopped)
    {
        this.onBuffer = onBuffer;
        this.onStopped = onStopped;
        thread = new Thread(Run) { IsBackground = true, Name = $"InterviewMaster.{device}.Capture" };
        thread.Start();
        return started.Task;
    }

    public Task StopAsync()
    {
        stopSignal.Set();
        return thread is null ? Task.CompletedTask : finished.Task;
    }

    public void StopSync()
    {
        stopSignal.Set();
        if (thread is { } running && running != Thread.CurrentThread) running.Join(JoinLimit);
    }

    private void Run()
    {
        try
        {
            Open();
        }
        catch (Exception error)
        {
            Close();
            started.TrySetException(error);
            finished.TrySetResult();
            return;
        }
        started.TrySetResult();

        Exception? failure = null;
        try
        {
            Pump();
        }
        catch (Exception error)
        {
            failure = error;
        }
        Close();
        finished.TrySetResult();
        if (failure is not null && !stopSignal.WaitOne(0)) onStopped?.Invoke(failure);
    }

    private void Pump()
    {
        var signals = new WaitHandle[] { stopSignal, dataSignal };
        var sinceData = Stopwatch.StartNew();
        var reopenAttempts = 0;
        while (true)
        {
            var signalled = WaitHandle.WaitAny(signals, 30);
            if (signalled == 0) return;

            if (reopenRequested)
            {
                reopenRequested = false;
                Reopen(ref reopenAttempts, new InvalidOperationException("The default audio device changed."));
                sinceData.Restart();
                continue;
            }

            int result;
            var delivered = false;
            try
            {
                result = Drain(ref delivered);
            }
            catch (COMException error)
            {
                result = error.HResult;
            }
            if (result < 0)
            {
                Reopen(ref reopenAttempts, Describe(result));
                sinceData.Restart();
                continue;
            }
            if (delivered)
            {
                reopenAttempts = 0;
                sinceData.Restart();
            }
            else if (device == CaptureDevice.SystemAudio && sinceData.ElapsedMilliseconds >= 30)
            {
                // Loopback delivers nothing while nothing plays. The speech detector needs to hear the
                // silence to end a turn, as it does from the continuous macOS stream.
                var frames = (int)Math.Min(format.SampleRate / 10, format.SampleRate * sinceData.ElapsedMilliseconds / 1000);
                sinceData.Restart();
                Deliver(IntPtr.Zero, frames, silent: true);
            }
        }
    }

    private int Drain(ref bool delivered)
    {
        var client = captureClient!;
        while (true)
        {
            var result = client.GetNextPacketSize(out var pending);
            if (result < 0) return result;
            if (pending == 0) return 0;
            result = client.GetBuffer(out var data, out var frames, out var flags, out _, out _);
            if (result < 0) return result;
            try
            {
                Deliver(data, (int)frames, (flags & Wasapi.BufferFlagsSilent) != 0);
                delivered = true;
            }
            finally
            {
                client.ReleaseBuffer(frames);
            }
            if (stopSignal.WaitOne(0)) return 0;
        }
    }

    private void Deliver(IntPtr data, int frames, bool silent)
    {
        if (frames <= 0) return;
        var bytes = frames * format.BytesPerFrame;
        if (buffer.Length < bytes) buffer = new byte[bytes];
        if (silent || data == IntPtr.Zero) Array.Clear(buffer, 0, bytes);
        else Marshal.Copy(data, buffer, 0, bytes);
        onBuffer?.Invoke(buffer.AsSpan(0, bytes), format);
    }

    /// A device that was unplugged or replaced as the default is opened again, a few times at most.
    private void Reopen(ref int attempts, Exception reason)
    {
        attempts++;
        if (attempts > MaxReopenAttempts) throw reason;
        Log.Print($"AUDIO: reopening {device.RawValue()} ({reason.Message}), attempt {attempts}");
        CloseStream();
        if (stopSignal.WaitOne(200 * attempts)) return;
        try
        {
            OpenStream();
        }
        catch (Exception error) when (attempts < MaxReopenAttempts)
        {
            Log.Print($"AUDIO: reopen failed: {error.Message}");
            reopenRequested = true;
        }
    }

    private void Open()
    {
        enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        OpenStream();
        // The microphone follows the default input, as AVAudioEngine does on a configuration change.
        if (device == CaptureDevice.Microphone)
        {
            listener = new DeviceChangeListener(() =>
            {
                reopenRequested = true;
                dataSignal.Set();
            });
            if (enumerator.RegisterEndpointNotificationCallback(listener) < 0) listener = null;
        }
    }

    private void OpenStream()
    {
        if (device == CaptureDevice.SystemAudio && TryOpenProcessLoopback()) return;
        OpenEndpoint();
    }

    private void OpenEndpoint()
    {
        var isMicrophone = device == CaptureDevice.Microphone;
        var result = enumerator!.GetDefaultAudioEndpoint(
            isMicrophone ? Wasapi.DataFlowCapture : Wasapi.DataFlowRender, Wasapi.RoleConsole, out var endpoint);
        if (result == Wasapi.NotFound)
        {
            throw new InvalidOperationException(isMicrophone ? "No microphone was found." : "No playback device was found.");
        }
        Check(result);
        try
        {
            var clientId = Wasapi.AudioClientId;
            Check(endpoint.Activate(ref clientId, Wasapi.ClsCtxAll, IntPtr.Zero, out var activated));
            var client = (IAudioClient)activated;
            audioClient = client;
            Check(client.GetMixFormat(out var mixFormat));
            try
            {
                format = ReadFormat(mixFormat);
                var flags = Wasapi.StreamFlagsEventCallback | (isMicrophone ? 0 : Wasapi.StreamFlagsLoopback);
                Check(client.Initialize(Wasapi.ShareModeShared, flags, 1_000_000, 0, mixFormat, IntPtr.Zero));
            }
            finally
            {
                Marshal.FreeCoTaskMem(mixFormat);
            }
            StartStream(client);
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    private bool TryOpenProcessLoopback()
    {
        var parameters = IntPtr.Zero;
        var variant = IntPtr.Zero;
        var waveFormat = IntPtr.Zero;
        try
        {
            parameters = Marshal.AllocHGlobal(Marshal.SizeOf<ProcessLoopbackActivation>());
            Marshal.StructureToPtr(new ProcessLoopbackActivation
            {
                ActivationType = Wasapi.ActivationTypeProcessLoopback,
                TargetProcessId = Environment.ProcessId,
                ProcessLoopbackMode = Wasapi.LoopbackExcludeTargetProcessTree
            }, parameters, false);
            variant = Marshal.AllocHGlobal(Marshal.SizeOf<BlobPropVariant>());
            Marshal.StructureToPtr(new BlobPropVariant
            {
                Type = Wasapi.VariantBlob,
                BlobSize = (uint)Marshal.SizeOf<ProcessLoopbackActivation>(),
                BlobData = parameters
            }, variant, false);

            var handler = new ActivationHandler();
            var clientId = Wasapi.AudioClientId;
            var result = Wasapi.ActivateAudioInterfaceAsync(Wasapi.ProcessLoopbackDevice, ref clientId, variant, handler, out var operation);
            if (result < 0) return Fallback(result);
            if (!handler.Completed.WaitOne(TimeSpan.FromSeconds(5))) return Fallback(unchecked((int)0x800705B4));
            result = operation.GetActivateResult(out var activateResult, out var activated);
            Marshal.ReleaseComObject(operation);
            if (result < 0) return Fallback(result);
            if (activateResult < 0 || activated is null) return Fallback(activateResult);

            var client = (IAudioClient)activated;
            // The process loopback device has no mix format; it delivers whatever is asked for.
            var requested = new PcmFormat(PcmConverter.OutputSampleRate, 2, PcmSampleKind.Float32);
            waveFormat = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
            Marshal.StructureToPtr(new WaveFormatEx
            {
                FormatTag = Wasapi.FormatIeeeFloat,
                Channels = (ushort)requested.Channels,
                SamplesPerSecond = (uint)requested.SampleRate,
                AverageBytesPerSecond = (uint)(requested.SampleRate * requested.BytesPerFrame),
                BlockAlign = (ushort)requested.BytesPerFrame,
                BitsPerSample = 32,
                ExtraSize = 0
            }, waveFormat, false);
            var flags = Wasapi.StreamFlagsLoopback | Wasapi.StreamFlagsEventCallback | Wasapi.StreamFlagsAutoConvertPcm;
            result = client.Initialize(Wasapi.ShareModeShared, flags, 200_000, 0, waveFormat, IntPtr.Zero);
            if (result < 0)
            {
                Marshal.ReleaseComObject(client);
                return Fallback(result);
            }
            audioClient = client;
            format = requested;
            StartStream(client);
            Log.Print("AUDIO: system audio through process loopback (this app's own sound excluded)");
            return true;
        }
        catch (Exception error) when (error is COMException or EntryPointNotFoundException or DllNotFoundException or InvalidCastException)
        {
            CloseStream();
            return Fallback(error.HResult);
        }
        finally
        {
            if (waveFormat != IntPtr.Zero) Marshal.FreeHGlobal(waveFormat);
            if (variant != IntPtr.Zero) Marshal.FreeHGlobal(variant);
            if (parameters != IntPtr.Zero) Marshal.FreeHGlobal(parameters);
        }
    }

    private static bool Fallback(int result)
    {
        Log.Print($"AUDIO: process loopback unavailable (0x{result:X8}); using the playback device loopback, which includes this app's own sound");
        return false;
    }

    private void StartStream(IAudioClient client)
    {
        Check(client.SetEventHandle(dataSignal.SafeWaitHandle.DangerousGetHandle()));
        var captureId = Wasapi.AudioCaptureClientId;
        Check(client.GetService(ref captureId, out var service));
        captureClient = (IAudioCaptureClient)service;
        Check(client.Start());
    }

    private static PcmFormat ReadFormat(IntPtr pointer)
    {
        var wave = Marshal.PtrToStructure<WaveFormatEx>(pointer);
        var tag = wave.FormatTag;
        if (tag == Wasapi.FormatExtensible && wave.ExtraSize >= 22)
        {
            var subFormat = Marshal.PtrToStructure<WaveFormatExtensible>(pointer).SubFormat;
            tag = subFormat == Wasapi.SubTypeIeeeFloat ? Wasapi.FormatIeeeFloat : subFormat == Wasapi.SubTypePcm ? Wasapi.FormatPcm : (ushort)0;
        }
        PcmSampleKind? kind = (tag, wave.BitsPerSample) switch
        {
            (Wasapi.FormatIeeeFloat, 32) => PcmSampleKind.Float32,
            (Wasapi.FormatPcm, 16) => PcmSampleKind.Int16,
            (Wasapi.FormatPcm, 24) => PcmSampleKind.Int24,
            (Wasapi.FormatPcm, 32) => PcmSampleKind.Int32,
            _ => null
        };
        if (kind is null || wave.Channels == 0 || wave.SamplesPerSecond == 0)
        {
            throw new CaptureFormatException("The audio device has no usable audio format.");
        }
        return new PcmFormat((int)wave.SamplesPerSecond, wave.Channels, kind.Value);
    }

    private void Check(int result)
    {
        if (result < 0) throw Describe(result);
    }

    private Exception Describe(int result)
    {
        if (result == Wasapi.AccessDenied && device == CaptureDevice.Microphone)
        {
            return new AudioCapturePermissionException(CaptureDevice.Microphone);
        }
        if (result == Wasapi.DeviceInvalidated) return new InvalidOperationException("The audio device was disconnected.");
        return Marshal.GetExceptionForHR(result) ?? new COMException("Audio capture failed.", result);
    }

    private void CloseStream()
    {
        try { audioClient?.Stop(); }
        catch (Exception error) { Log.Print($"AUDIO: stop failed: {error.Message}"); }
        if (captureClient is not null) Marshal.ReleaseComObject(captureClient);
        if (audioClient is not null) Marshal.ReleaseComObject(audioClient);
        captureClient = null;
        audioClient = null;
    }

    private void Close()
    {
        CloseStream();
        if (enumerator is not null)
        {
            if (listener is not null) enumerator.UnregisterEndpointNotificationCallback(listener);
            Marshal.ReleaseComObject(enumerator);
        }
        listener = null;
        enumerator = null;
    }

    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public ManualResetEvent Completed { get; } = new(false);

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            Completed.Set();
            return 0;
        }
    }

    private sealed class DeviceChangeListener(Action defaultInputChanged) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, int newState) => 0;

        public int OnDeviceAdded(string deviceId) => 0;

        public int OnDeviceRemoved(string deviceId) => 0;

        public int OnDefaultDeviceChanged(int dataFlow, int role, string? defaultDeviceId)
        {
            if (dataFlow == Wasapi.DataFlowCapture && role == Wasapi.RoleConsole) defaultInputChanged();
            return 0;
        }

        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;
    }
}
