using System.Buffers.Binary;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Tests;

/// Synthetic PCM only: nothing here opens a device or records sound. The format half is a port of
/// Tests/test_microphone_format.swift.
public sealed class PcmCaptureTests : SwiftStyleTests
{
    private readonly QueueMainThread main = new();

    public override void Dispose()
    {
        main.Dispose();
        base.Dispose();
    }

    private static byte[] Tone(PcmFormat format, int frames, float amplitude = 0.2f)
    {
        var bytes = new byte[frames * format.BytesPerFrame];
        for (var frame = 0; frame < frames; frame++)
        {
            var value = amplitude * MathF.Sin(frame * 2 * MathF.PI * 440 / format.SampleRate);
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var slot = bytes.AsSpan((frame * format.Channels + channel) * format.BytesPerSample, format.BytesPerSample);
                switch (format.Kind)
                {
                    case PcmSampleKind.Float32:
                        BinaryPrimitives.WriteSingleLittleEndian(slot, value);
                        break;
                    case PcmSampleKind.Int16:
                        BinaryPrimitives.WriteInt16LittleEndian(slot, (short)(value * 32767));
                        break;
                    case PcmSampleKind.Int24:
                        var wide = (int)(value * 8388607);
                        slot[0] = (byte)wide;
                        slot[1] = (byte)(wide >> 8);
                        slot[2] = (byte)(wide >> 16);
                        break;
                    default:
                        BinaryPrimitives.WriteInt32LittleEndian(slot, (int)(value * 2147483647.0));
                        break;
                }
            }
        }
        return bytes;
    }

    public static IEnumerable<object[]> Formats()
    {
        foreach (var rate in new[] { 16_000, 44_100, 48_000, 96_000 })
        {
            foreach (var channels in new[] { 1, 2 })
            {
                foreach (var kind in new[] { PcmSampleKind.Float32, PcmSampleKind.Int16, PcmSampleKind.Int24, PcmSampleKind.Int32 })
                {
                    yield return new object[] { rate, channels, kind };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void EveryDeviceFormatBecomesMono48k(int rate, int channels, PcmSampleKind kind)
    {
        var format = new PcmFormat(rate, channels, kind);
        var converter = new PcmConverter(format);
        var buffer = Tone(format, rate / 50);
        var samples = new List<float>();
        for (var pass = 0; pass < 50; pass++) samples.AddRange(converter.Convert(buffer).ToArray());

        Check(Math.Abs(samples.Count - 48_000) < 200, $"resampler preserves one-second duration: {samples.Count}");
        Check(samples.Any(sample => Math.Abs(sample) > 0.1), "conversion preserves audible signal");
        Check(samples.All(float.IsFinite), "converted PCM samples remain finite");
        Check(samples.All(sample => Math.Abs(sample) <= 0.21), "conversion does not add level");

        var time = 0.0;
        byte[]? committed = null;
        var detector = new SpeechTurnDetector(
            SpeechTurnDetector.Labels.Microphone, SpeechTurnDetector.Tuning.MicrophoneExperimental, "FormatTest",
            (emit, wav) => { if (emit == SpeechTurnEmit.FinalSilence) committed = wav; },
            (_, _) => { }, _ => { },
            () => DateTime.UnixEpoch.AddSeconds(time));
        detector.SetSampleRate(PcmConverter.OutputSampleRate);
        detector.Ingest(new[] { 0.2f });
        time = 1;
        detector.Ingest(samples.ToArray());
        time = 1.7;
        detector.Ingest(new[] { 0f });
        Check(committed is not null, "converted speech reaches detector");
        if (committed is null) return;
        Check(BinaryPrimitives.ReadUInt32LittleEndian(committed.AsSpan(24, 4)) == 48_000, "converted samples and WAV header agree on output rate");
        Check(committed.Length == 44 + (samples.Count + 1) * 2, "WAV contains converted frame count");
    }

    [Fact]
    public void ConverterKeepsPhaseAcrossUnevenBuffers()
    {
        var format = new PcmFormat(44_100, 1, PcmSampleKind.Float32);
        var whole = new PcmConverter(format).Convert(Tone(format, 44_100)).ToArray();
        var pieces = new List<float>();
        var chunked = new PcmConverter(format);
        var all = Tone(format, 44_100);
        var offset = 0;
        foreach (var frames in Enumerable.Range(0, 400).Select(index => 1 + index % 337))
        {
            var take = Math.Min(frames * 4, all.Length - offset);
            if (take <= 0) break;
            pieces.AddRange(chunked.Convert(all.AsSpan(offset, take)).ToArray());
            offset += take;
        }
        pieces.AddRange(chunked.Convert(all.AsSpan(offset)).ToArray());
        Check(pieces.Count == whole.Length, $"buffer boundaries neither drop nor repeat samples: {pieces.Count} vs {whole.Length}");
        Check(pieces.Zip(whole).All(pair => Math.Abs(pair.First - pair.Second) < 1e-4), "chunked output equals one-shot output");
    }

    [Fact]
    public void UnusableFormatIsRejected()
    {
        var threw = false;
        try { _ = new PcmConverter(new PcmFormat(0, 2, PcmSampleKind.Float32)); }
        catch (CaptureFormatException) { threw = true; }
        Check(threw, "a device without a sample rate is reported, not divided by");
        Check(new PcmConverter(PcmFormat.Mono48k).IsPassthrough, "48 kHz mono float needs no conversion");
        Check(new PcmConverter(PcmFormat.Mono48k).Convert(new byte[3]).Length == 0, "a partial frame is ignored");
    }

    private sealed class FakeSource : IPcmSource
    {
        public TaskCompletionSource? StartGate { get; init; }
        public Exception? StartError { get; init; }
        public PcmBufferHandler? OnBuffer { get; private set; }
        public Action<Exception>? OnStopped { get; private set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int SyncStops { get; private set; }

        public async Task StartAsync(PcmBufferHandler onBuffer, Action<Exception> onStopped)
        {
            Starts++;
            OnBuffer = onBuffer;
            OnStopped = onStopped;
            if (StartGate is not null) await StartGate.Task;
            if (StartError is not null) throw StartError;
        }

        public Task StopAsync()
        {
            Stops++;
            return Task.CompletedTask;
        }

        public void StopSync() => SyncStops++;
    }

    private sealed class Recorder
    {
        public List<string> Statuses { get; } = new();
        public List<byte[]> Segments { get; } = new();
        public List<Exception> Failures { get; } = new();
        public int Levels { get; set; }

        public void Attach(PcmCapture capture)
        {
            capture.OnStatusChange = Statuses.Add;
            capture.OnSpeechSegment = Segments.Add;
            capture.OnCaptureFailed = Failures.Add;
            capture.OnLevelUpdate = (_, _) => Levels++;
        }
    }

    private static Task Allowed(CaptureDevice device, bool prompt) => Task.CompletedTask;

    private Task Drain() => main.RunAsync(() => { });

    [Theory]
    [InlineData(CaptureDevice.Microphone)]
    [InlineData(CaptureDevice.SystemAudio)]
    public async Task SpeechFromTheDeviceBecomesOneSegment(CaptureDevice device)
    {
        var time = 0.0;
        var source = new FakeSource();
        var capture = new PcmCapture(device, main, () => source, Allowed, () => DateTime.UnixEpoch.AddSeconds(time));
        var recorder = new Recorder();
        recorder.Attach(capture);
        await capture.StartCapturingAsync();
        await Drain();
        var idle = device == CaptureDevice.Microphone ? SpeechTurnDetector.Labels.Microphone.Idle : SpeechTurnDetector.Labels.SystemAudio.Idle;
        Check(recorder.Statuses.SequenceEqual(new[] { idle }), "a started capture says it is listening");

        var stereo = new PcmFormat(44_100, 2, PcmSampleKind.Int16);
        source.OnBuffer!(Tone(stereo, 1), stereo);
        time = 1;
        source.OnBuffer!(Tone(stereo, 44_100), stereo);
        time = 1.7;
        var mono = PcmFormat.Mono48k;
        source.OnBuffer!(new byte[mono.BytesPerFrame * 2], mono);
        await Drain();
        Check(recorder.Segments.Count == 1, "speech followed by silence commits one segment");
        Check(recorder.Segments.Count == 1 && BinaryPrimitives.ReadUInt32LittleEndian(recorder.Segments[0].AsSpan(24, 4)) == 48_000, "the segment is a 48 kHz WAV whatever the device delivered");
        Check(recorder.Levels > 0, "the level meter is fed");

        await capture.StopCapturingAsync();
        var segments = recorder.Segments.Count;
        source.OnBuffer!(Tone(stereo, 44_100), stereo);
        await Drain();
        Check(source.Stops == 1 && recorder.Segments.Count == segments, "a stopped capture closes the device and ignores late buffers");
        var restartRefused = false;
        try { await capture.StartCapturingAsync(); }
        catch (OperationCanceledException) { restartRefused = true; }
        Check(restartRefused, "a stopped adapter is never restarted; the coordinator makes a fresh one");
    }

    [Fact]
    public async Task StopDuringStartLeavesNoOpenDevice()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeSource { StartGate = gate };
        var capture = new PcmCapture(CaptureDevice.Microphone, main, () => source, Allowed);
        var recorder = new Recorder();
        recorder.Attach(capture);
        var start = capture.StartCapturingAsync();
        Check(source.Starts == 1, "the device is opening");
        capture.StopCapturingSync();
        gate.SetResult();
        var cancelled = false;
        try { await start; }
        catch (OperationCanceledException) { cancelled = true; }
        await Drain();
        Check(cancelled, "the suspended start reports that it was cancelled");
        Check(source.SyncStops == 1 && source.Stops == 1, "the device that finished opening late is closed");
        Check(recorder.Statuses.Count == 0 && recorder.Failures.Count == 0, "a cancelled start says nothing");
    }

    [Fact]
    public async Task PermissionAndOpenFailuresAreThrownToTheCaller()
    {
        var opened = 0;
        var denied = new PcmCapture(CaptureDevice.Microphone, main, () => { opened++; return new FakeSource(); },
            (device, _) => Task.FromException(new AudioCapturePermissionException(device)));
        Exception? error = null;
        try { await denied.StartCapturingAsync(); }
        catch (Exception caught) { error = caught; }
        Check(error is AudioCapturePermissionException && error.Message == PlatformCopy.MicrophonePermissionHelp, "a denied microphone names the setting to change");
        Check(opened == 0, "the device is not touched before permission is known");

        var broken = new FakeSource { StartError = new InvalidOperationException("no device") };
        var failing = new PcmCapture(CaptureDevice.SystemAudio, main, () => broken, Allowed);
        error = null;
        try { await failing.StartCapturingAsync(); }
        catch (Exception caught) { error = caught; }
        Check(error?.Message == "no device" && broken.Stops == 1, "a device that fails to open is closed and the reason is kept");
    }

    [Theory]
    [InlineData(CaptureDevice.Microphone, "Microphone unavailable: unplugged", 1)]
    [InlineData(CaptureDevice.SystemAudio, "⚠️ System audio stopped: unplugged", 0)]
    public async Task DeviceThatEndsOnItsOwnIsReportedOnce(CaptureDevice device, string status, int syncStops)
    {
        var source = new FakeSource();
        var capture = new PcmCapture(device, main, () => source, Allowed);
        var recorder = new Recorder();
        recorder.Attach(capture);
        await capture.StartCapturingAsync();
        source.OnStopped!(new InvalidOperationException("unplugged"));
        source.OnStopped!(new InvalidOperationException("again"));
        await Drain();
        Check(recorder.Failures.Count == 1 && recorder.Failures[0].Message == "unplugged", "the failure reaches the coordinator once");
        Check(recorder.Statuses.LastOrDefault() == status, $"the status says what stopped: {recorder.Statuses.LastOrDefault()}");
        Check(source.SyncStops == syncStops, "the microphone is closed before the failure is reported");
    }

    [Fact]
    public async Task MuteSetBeforeStartIsShownAndKept()
    {
        var time = 0.0;
        var source = new FakeSource();
        var capture = new PcmCapture(CaptureDevice.SystemAudio, main, () => source, Allowed, () => DateTime.UnixEpoch.AddSeconds(time));
        var recorder = new Recorder();
        recorder.Attach(capture);
        capture.SetMuted(true);
        await capture.StartCapturingAsync();
        var format = PcmFormat.Mono48k;
        source.OnBuffer!(Tone(format, 1), format);
        time = 1;
        source.OnBuffer!(Tone(format, 48_000), format);
        time = 1.7;
        source.OnBuffer!(new byte[8], format);
        await Drain();
        Check(recorder.Statuses.Contains(SpeechTurnDetector.Labels.SystemAudio.Muted), "a muted capture starts muted");
        Check(recorder.Segments.Count == 0, "muted speech is never sent on");
    }
}
