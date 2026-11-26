using InterviewMaster.Domain;

namespace InterviewMaster.Tests.Domain;

/// From `Tests/test_speech_turn.swift`. The golden WAV bytes were frozen from the macOS detector.
public class SpeechTurnDetectorTests : SwiftStyleTests
{
    private sealed record Turn(SpeechTurnEmit Kind, double Time, string? Wav);

    private sealed class Harness
    {
        public double Time;
        public readonly List<Turn> Turns = new();
        public readonly List<(float Db, bool Speaking)> Levels = new();
        public readonly List<string> Statuses = new();
        public readonly SpeechTurnDetector Detector;

        public Harness(SpeechTurnDetector.Tuning? tuning = null, DateTime? epoch = null)
        {
            var start = epoch ?? new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Detector = new SpeechTurnDetector(
                SpeechTurnDetector.Labels.SystemAudio, tuning ?? SpeechTurnDetector.Tuning.SystemAudio, "TestVAD",
                emitTurn: (kind, wav) => Turns.Add(new Turn(kind, Time, wav is null ? null : Convert.ToBase64String(wav))),
                emitLevel: (db, speaking) => Levels.Add((db, speaking)),
                emitStatus: Statuses.Add,
                now: () => start.AddSeconds(Time));
        }

        public void Feed(double at, float[] samples)
        {
            Time = at;
            Detector.Ingest(samples);
        }

        public void Voice(double at) => Feed(at, new[] { 0.1f, -0.1f, 0.1f, -0.1f });

        public void Silence(double at) => Feed(at, new float[] { 0, 0, 0, 0 });

        public IEnumerable<SpeechTurnEmit> Kinds => Turns.Select(turn => turn.Kind);
    }

    private static SpeechTurnDetector.Tuning MakeTuning(double minDuration = 0.5, float floor = -35) => new(
        SpeechMargin: 18, SilenceMargin: 10, AbsoluteMinSpeechDb: floor,
        MinSpeechDuration: minDuration, SilenceTimeout: 0.65,
        SpeculativeSilenceTimeout: 0.25, BaselineWindowSize: 40,
        BaselineUpdateInterval: 5, LevelCallbackInterval: 0.08);

    [Fact]
    public void GoldenEventsAndWavBytes()
    {
        var golden = new Harness();
        var signals = new (double At, float[] Samples)[]
        {
            (0, new[] { 0.1f, -0.1f, 0.25f, -0.25f }), (0.25, new[] { 0.2f, -0.2f, 0.4f, -0.4f }),
            (0.5, new[] { 1.1f, -1.1f, 0.5f, -0.5f }), (0.749, new float[] { 0, 0, 0, 0 }),
            (0.75, new float[] { 0, 0, 0, 0 }), (0.751, new float[] { 0, 0, 0, 0 }),
            (0.8, new[] { 0.3f, -0.3f, 0.15f, -0.15f }), (1.049, new float[] { 0, 0, 0, 0 }),
            (1.05, new float[] { 0, 0, 0, 0 }), (1.051, new float[] { 0, 0, 0, 0 }),
            (1.449, new float[] { 0, 0, 0, 0 }), (1.45, new float[] { 0, 0, 0, 0 }), (1.451, new float[] { 0, 0, 0, 0 })
        };
        foreach (var (at, samples) in signals) golden.Feed(at, samples);
        var expected = new[]
        {
            new Turn(SpeechTurnEmit.SpeculativePreview, 0.751, "UklGRkwAAABXQVZFZm10IBAAAAABAAEAgLsAAAB3AQACABAAZGF0YSgAAACZGWfmMjPOzP9/AYD/PwHAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
            new Turn(SpeechTurnEmit.SpeechResumedAfterPreview, 0.8, null),
            new Turn(SpeechTurnEmit.SpeculativePreview, 1.051, "UklGRmwAAABXQVZFZm10IBAAAAABAAEAgLsAAAB3AQACABAAZGF0YUgAAACZGWfmMjPOzP9/AYD/PwHAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAZiaa2TMTzewAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="),
            new Turn(SpeechTurnEmit.FinalSilence, 1.451, "UklGRoQAAABXQVZFZm10IBAAAAABAAEAgLsAAAB3AQACABAAZGF0YWAAAACZGWfmMjPOzP9/AYD/PwHAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAZiaa2TMTzewAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")
        };
        Check(golden.Turns.Count == expected.Length, $"legacy event count is unchanged: {string.Join(", ", golden.Turns.Select(turn => $"{turn.Kind}@{turn.Time}"))}");
        for (var index = 0; index < Math.Min(expected.Length, golden.Turns.Count); index++)
        {
            Check(golden.Turns[index] == expected[index], $"legacy event {index} keeps its kind, time and WAV bytes: {golden.Turns[index]}");
        }
    }

    [Fact]
    public void Boundaries()
    {
        var boundaries = new Harness();
        boundaries.Voice(-1);
        boundaries.Voice(0);
        boundaries.Silence(0.249);
        Check(boundaries.Turns.Count == 0, "no preview before 0.25s");
        boundaries.Silence(0.25);
        Check(boundaries.Turns.Count == 0, "no preview exactly at 0.25s");
        boundaries.Silence(0.251);
        Check(boundaries.Kinds.SequenceEqual(new[] { SpeechTurnEmit.SpeculativePreview }), "preview strictly after 0.25s");
        boundaries.Silence(0.649);
        boundaries.Silence(0.65);
        Check(boundaries.Turns.Count == 1, "no final before or exactly at 0.65s");
        boundaries.Silence(0.651);
        Check(boundaries.Kinds.SequenceEqual(new[] { SpeechTurnEmit.SpeculativePreview, SpeechTurnEmit.FinalSilence }), "final strictly after 0.65s");

        var trailing = new Harness();
        trailing.Voice(0);
        trailing.Voice(0.3);
        trailing.Silence(0.56);
        Check(trailing.Kinds.SequenceEqual(new[] { SpeechTurnEmit.SpeculativePreview }), "trailing silence counts toward elapsed minimum duration");
        trailing.Silence(0.96);
        Check(trailing.Turns.LastOrDefault()?.Kind == SpeechTurnEmit.FinalSilence, "0.3s spoken clip commits after eligible trailing silence");

        var minimum = new Harness();
        minimum.Voice(0);
        minimum.Silence(0.499);
        minimum.Silence(0.5);
        Check(minimum.Turns.Count == 0, "duration minimum is strict, including exactly 0.5s");
        minimum.Silence(0.501);
        Check(minimum.Kinds.SequenceEqual(new[] { SpeechTurnEmit.SpeculativePreview }), "elapsed duration over minimum previews");
        var oldEpoch = new Harness(MakeTuning(minDuration: 2), new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        oldEpoch.Voice(0);
        oldEpoch.Voice(0.1);
        oldEpoch.Silence(0.8);
        Check(oldEpoch.Turns.Count == 0, "eligibility uses injected 1970 clock, never wall time");
    }

    [Fact]
    public void MuteAndReset()
    {
        var muted = new Harness();
        muted.Voice(0);
        muted.Voice(0.3);
        muted.Silence(0.56);
        muted.Detector.SetMuted(true);
        Check(muted.Kinds.SequenceEqual(new[] { SpeechTurnEmit.SpeculativePreview, SpeechTurnEmit.SpeechResumedAfterPreview }), "mute cancels speculative preview");
        Check(muted.Statuses.LastOrDefault() == "Capture muted" && muted.Levels.Last() == (-100f, false), "mute reports its status and a silent level");
        muted.Silence(2);
        Check(!muted.Kinds.Contains(SpeechTurnEmit.FinalSilence), "mute abandons current utterance without committing");
        muted.Detector.Reset();
        var levelCount = muted.Levels.Count;
        muted.Voice(3);
        Check(muted.Levels.Count == levelCount, "configuration reset preserves mute before level/RMS work");
        muted.Detector.SetMuted(false);
        muted.Voice(4);
        muted.Silence(4.7);
        Check(muted.Turns.LastOrDefault()?.Kind == SpeechTurnEmit.FinalSilence, "unmute starts a fresh utterance");

        var recovery = new Harness();
        recovery.Voice(0);
        recovery.Voice(0.3);
        recovery.Silence(0.56);
        recovery.Detector.Reset();
        Check(recovery.Kinds.SequenceEqual(new[] { SpeechTurnEmit.SpeculativePreview, SpeechTurnEmit.SpeechResumedAfterPreview }), "configuration reset cancels an abandoned preview");
        recovery.Silence(2);
        Check(!recovery.Kinds.Contains(SpeechTurnEmit.FinalSilence), "configuration reset cannot commit the old utterance");

        var baseline = new Harness(MakeTuning(floor: -90));
        baseline.Detector.SetMuted(true);
        for (var tick = 0; tick < 200; tick++) baseline.Feed(tick / 100.0, new[] { 0.03162278f, -0.03162278f });
        baseline.Detector.SetMuted(false);
        var quietVoice = new[] { 0.01258925f, -0.01258925f }; // -38 dB; above preserved -60 dB baseline + 18.
        baseline.Feed(3, quietVoice);
        baseline.Feed(3.3, quietVoice);
        baseline.Silence(4);
        Check(baseline.Turns.LastOrDefault()?.Kind == SpeechTurnEmit.FinalSilence, "muted sound cannot raise the baseline and deafen subsequent speech");
    }

    [Fact]
    public void HeaderShutdownAndSinks()
    {
        var header = new Harness();
        header.Detector.SetSampleRate(44_100);
        header.Voice(0);
        header.Silence(0.7);
        var wav = Convert.FromBase64String(header.Turns.Last().Wav!);
        Check(BitConverter.ToUInt32(wav, 24) == 44_100, "WAV header carries detector sample rate");
        Check(System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF", "WAV RIFF header");
        var priorTurns = header.Turns.Count;
        header.Detector.Shutdown();
        header.Voice(2);
        header.Silence(3);
        Check(header.Turns.Count == priorTurns, "shutdown suppresses every future signal");

        // C# locks are re-entrant, so the proof that sinks run unlocked is another thread getting in.
        SpeechTurnDetector? reentrant = null;
        var callbackRan = false;
        reentrant = new SpeechTurnDetector(
            SpeechTurnDetector.Labels.SystemAudio, SpeechTurnDetector.Tuning.SystemAudio, "Reentrant",
            emitTurn: (_, _) => { },
            emitLevel: (_, _) =>
            {
                var other = new Thread(() => reentrant!.SetSampleRate(48_000));
                other.Start();
                callbackRan = other.Join(TimeSpan.FromSeconds(5));
            },
            emitStatus: _ => { });
        reentrant.Ingest(new[] { 0.1f });
        Check(callbackRan, "sinks execute outside detector state lock");
    }
}
