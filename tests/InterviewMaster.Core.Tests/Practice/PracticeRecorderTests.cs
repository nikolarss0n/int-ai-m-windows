using System.Buffers.Binary;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;

namespace InterviewMaster.Tests;

/// Synthetic PCM only: nothing here opens a device. The recorder has no macOS counterpart to port
/// tests from (the Mac records through AVAudioRecorder), so these pin what the transcriber is sent.
public sealed class PracticeRecorderTests : SwiftStyleTests
{
    private sealed class FakeSource(bool failsToStart = false) : IPcmSource
    {
        public PcmBufferHandler? Deliver { get; private set; }

        public int Stops { get; private set; }

        public Task StartAsync(PcmBufferHandler onBuffer, Action<Exception> onStopped)
        {
            if (failsToStart) return Task.FromException(new InvalidOperationException("no device"));
            Deliver = onBuffer;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Stops++;
            return Task.CompletedTask;
        }

        public void StopSync() => Stops++;
    }

    private static byte[] Constant(PcmFormat format, int frames, float value)
    {
        var bytes = new byte[frames * format.BytesPerFrame];
        for (var index = 0; index < frames * format.Channels; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 4, 4), value);
        }
        return bytes;
    }

    [Fact]
    public async Task RecordsSixteenKilohertzMonoWav()
    {
        var source = new FakeSource();
        var recorder = new PcmPracticeRecorder(() => source, () => Task.CompletedTask);
        Check(await recorder.RequestAccessAsync(), "access is granted when the preflight passes");
        Check(recorder.Recording is null, "nothing is recorded before a start");
        Check(await recorder.StartAsync(), "the recorder starts");

        var format = new PcmFormat(48_000, 2, PcmSampleKind.Float32);
        source.Deliver!(Constant(format, 4_800, 0.5f), format);
        // A buffer that does not end on a whole output sample carries over to the next one.
        source.Deliver!(Constant(format, 100, 0.5f), format);
        source.Deliver!(Constant(format, 200, 0.5f), format);
        recorder.Stop();
        Check(source.Stops == 1, "stopping stops the device once");

        var wav = recorder.Recording!;
        var samples = (wav.Length - 44) / 2;
        Check(samples == 5_100 / 3, $"48 kHz becomes 16 kHz: {samples} samples");
        Check(GroqRequestTuning.TranscriptionUpload(wav) == ("audio.wav", "audio/wav"), "the upload is named a WAV");
        Check(BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)) == wav.Length - 8, "the RIFF size covers the file");
        Check(BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)) == 1, "one channel");
        Check(BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)) == 16_000, "16 kHz");
        Check(BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)) == 16, "16 bits");
        Check(BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)) == samples * 2, "the data size is the samples");
        var last = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(wav.Length - 2));
        Check(Math.Abs(last - 16_384) <= 2, $"half scale stays half scale: {last}");

        source.Deliver!(Constant(format, 480, 0.5f), format);
        Check(recorder.Recording!.Length == wav.Length, "buffers after the stop are not recorded");
        recorder.Discard();
        Check(recorder.Recording is null, "discarding empties the recording");
    }

    [Fact]
    public async Task ANewStartReplacesTheLastRecording()
    {
        var sources = new List<FakeSource>();
        var recorder = new PcmPracticeRecorder(
            () =>
            {
                sources.Add(new FakeSource());
                return sources[^1];
            },
            () => Task.CompletedTask);
        var format = PcmFormat.Mono48k;
        await recorder.StartAsync();
        sources[0].Deliver!(Constant(format, 480, 0.25f), format);
        Check(await recorder.StartAsync(), "a second start succeeds");
        Check(sources[0].Stops == 1, "the first device is stopped");
        Check(recorder.Recording is null, "the first recording is gone");
        sources[0].Deliver!(Constant(format, 480, 0.25f), format);
        Check(recorder.Recording is null, "the old device can no longer record");
        sources[1].Deliver!(Constant(format, 480, 0.25f), format);
        Check(recorder.Recording is { Length: 44 + 320 }, "the new device records");
    }

    [Fact]
    public async Task RefusalAndFailureAreReportedNotThrown()
    {
        var refused = new PcmPracticeRecorder(
            () => new FakeSource(), () => Task.FromException(new AudioCapturePermissionException(CaptureDevice.Microphone)));
        Check(!await refused.RequestAccessAsync(), "a refused microphone is reported");

        var broken = new PcmPracticeRecorder(() => new FakeSource(failsToStart: true), () => Task.CompletedTask);
        Check(!await broken.StartAsync(), "a device that will not open is reported");
        Check(broken.Recording is null, "a failed start records nothing");

        var missing = new PcmPracticeRecorder(() => throw new InvalidOperationException("no device"), () => Task.CompletedTask);
        Check(!await missing.StartAsync(), "a missing device is reported");
    }

    [Fact]
    public async Task RecordingStopsGrowingAtTheUploadLimit()
    {
        var source = new FakeSource();
        var recorder = new PcmPracticeRecorder(() => source, () => Task.CompletedTask);
        await recorder.StartAsync();
        var format = PcmFormat.Mono48k;
        var second = Constant(format, 48_000 * 60, 0.1f);
        for (var minute = 0; minute < 14; minute++) source.Deliver!(second, format);
        var length = recorder.Recording!.Length;
        Check(length >= PcmPracticeRecorder.MaximumBytes && length < 25_000_000, $"the file stays under the transcriber's limit: {length}");
    }
}
