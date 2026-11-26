using System.Globalization;
using System.Text;
using InterviewMaster.Support;

namespace InterviewMaster.Domain;

/// Capture-independent turn detection. All state is serialized; sinks run after state is unlocked.
public sealed class SpeechTurnDetector
{
    public sealed record Labels(string Idle, string Speaking, string Processing, string Muted)
    {
        public static readonly Labels SystemAudio = new("🔊 Listening to system audio...", "🗣 Interviewer speaking...", "⏳ Processing...", "Capture muted");
        public static readonly Labels Microphone = new("Listening to microphone...", "🗣 Interviewer speaking...", "⏳ Processing...", "Capture muted");
    }

    public sealed record Tuning(
        float SpeechMargin, float SilenceMargin, float AbsoluteMinSpeechDb,
        double MinSpeechDuration, double SilenceTimeout, double SpeculativeSilenceTimeout,
        int BaselineWindowSize, int BaselineUpdateInterval, double LevelCallbackInterval)
    {
        public static readonly Tuning SystemAudio = new(
            SpeechMargin: 18, SilenceMargin: 10, AbsoluteMinSpeechDb: -35,
            MinSpeechDuration: AppConstants.Thresholds.MinSpeechDuration,
            SilenceTimeout: AppConstants.Thresholds.SilenceTimeout,
            SpeculativeSilenceTimeout: AppConstants.Thresholds.SpeculativeSilenceTimeout,
            BaselineWindowSize: 40, BaselineUpdateInterval: 5, LevelCallbackInterval: 0.08);

        /// UNMEASURED experiment. This conservative starting point is not room-microphone calibration.
        public static readonly Tuning MicrophoneExperimental = SystemAudio;
    }

    private enum EmissionKind
    {
        Turn,
        Level,
        Status
    }

    private readonly record struct Emission(EmissionKind Kind, SpeechTurnEmit Emit = default, byte[]? Audio = null, float Db = 0, bool Speaking = false, string Text = "")
    {
        public static Emission Turn(SpeechTurnEmit emit, byte[]? audio) => new(EmissionKind.Turn, Emit: emit, Audio: audio);

        public static Emission Level(float db, bool speaking) => new(EmissionKind.Level, Db: db, Speaking: speaking);

        public static Emission Status(string text) => new(EmissionKind.Status, Text: text);
    }

    private static readonly List<Emission> NoEmissions = new();

    private readonly object stateGate = new();
    private readonly Labels labels;
    private readonly Tuning tuning;
    private readonly string logTag;
    private readonly Func<DateTime> now;
    private Action<SpeechTurnEmit, byte[]?>? turnSink;
    private Action<float, bool>? levelSink;
    private Action<string>? statusSink;
    private readonly List<float> baselineBuffer = new();
    private float currentBaseline = -60;
    private int baselineUpdateCount;
    private bool isSpeaking;
    private bool isMuted;
    private bool isShutdown;
    private DateTime? speechStartTime;
    private DateTime? lastSpeechTime;
    private float peakLevel = -100;
    private List<float> recordedSamples = new();
    private double sampleRate = 48_000;
    private bool didPreviewCurrentUtterance;
    private DateTime lastLevelCallbackTime = DateTime.MinValue;
    private bool lastLevelCallbackSpeaking;
    private int checkCount;

    public SpeechTurnDetector(
        Labels labels, Tuning tuning, string logTag,
        Action<SpeechTurnEmit, byte[]?> emitTurn, Action<float, bool> emitLevel, Action<string> emitStatus,
        Func<DateTime>? now = null)
    {
        this.labels = labels;
        this.tuning = tuning;
        this.logTag = logTag;
        this.now = now ?? (() => DateTime.UtcNow);
        turnSink = emitTurn;
        levelSink = emitLevel;
        statusSink = emitStatus;
    }

    public void Ingest(ReadOnlySpan<float> samples)
    {
        List<Emission> emissions;
        Action<SpeechTurnEmit, byte[]?>? turn;
        Action<float, bool>? level;
        Action<string>? status;
        lock (stateGate)
        {
            if (isShutdown || isMuted)
            {
                emissions = NoEmissions;
            }
            else
            {
                float sum = 0;
                foreach (var sample in samples) sum += sample * sample;
                var rms = MathF.Sqrt(sum / Math.Max(samples.Length, 1));
                var db = 20 * MathF.Log10(MathF.Max(rms, 0.0000001f));
                // Preserve the legacy policy: the triggering frame itself is not recorded.
                if (isSpeaking) recordedSamples.AddRange(samples);
                emissions = ProcessVAD(db);
            }
            (turn, level, status) = (turnSink, levelSink, statusSink);
        }
        Deliver(emissions, turn, level, status);
    }

    public void SetSampleRate(double rate)
    {
        lock (stateGate)
        {
            if (double.IsFinite(rate) && rate > 0 && rate <= uint.MaxValue / 2) sampleRate = rate;
        }
    }

    public void SetMuted(bool muted)
    {
        Mutate(() =>
        {
            if (isShutdown || isMuted == muted) return NoEmissions;
            isMuted = muted;
            var emissions = new List<Emission>();
            if (muted)
            {
                if (didPreviewCurrentUtterance) emissions.Add(Emission.Turn(SpeechTurnEmit.SpeechResumedAfterPreview, null));
                AbandonCurrentUtterance();
                lastLevelCallbackSpeaking = false;
                emissions.Add(Emission.Level(-100, false));
            }
            emissions.Add(Emission.Status(muted ? labels.Muted : labels.Idle));
            return emissions;
        });
    }

    /// Reinitialize signal history while preserving the externally owned mute gate.
    public void Reset()
    {
        Mutate(() =>
        {
            var cancelledPreview = didPreviewCurrentUtterance;
            AbandonCurrentUtterance();
            baselineBuffer.Clear();
            currentBaseline = -60;
            baselineUpdateCount = 0;
            checkCount = 0;
            lastLevelCallbackTime = DateTime.MinValue;
            lastLevelCallbackSpeaking = false;
            return cancelledPreview
                ? new List<Emission> { Emission.Turn(SpeechTurnEmit.SpeechResumedAfterPreview, null) }
                : NoEmissions;
        });
    }

    public void Shutdown()
    {
        lock (stateGate)
        {
            isShutdown = true;
            AbandonCurrentUtterance();
            turnSink = null;
            levelSink = null;
            statusSink = null;
        }
    }

    private void Mutate(Func<List<Emission>> body)
    {
        List<Emission> emissions;
        Action<SpeechTurnEmit, byte[]?>? turn;
        Action<float, bool>? level;
        Action<string>? status;
        lock (stateGate)
        {
            emissions = body();
            (turn, level, status) = (turnSink, levelSink, statusSink);
        }
        Deliver(emissions, turn, level, status);
    }

    private static void Deliver(List<Emission> emissions, Action<SpeechTurnEmit, byte[]?>? turn, Action<float, bool>? level, Action<string>? status)
    {
        foreach (var emission in emissions)
        {
            switch (emission.Kind)
            {
                case EmissionKind.Turn: turn?.Invoke(emission.Emit, emission.Audio); break;
                case EmissionKind.Level: level?.Invoke(emission.Db, emission.Speaking); break;
                default: status?.Invoke(emission.Text); break;
            }
        }
    }

    private void UpdateBaseline(float db)
    {
        if (isSpeaking || db <= -90 || db >= -20) return;
        baselineUpdateCount++;
        if (baselineUpdateCount < tuning.BaselineUpdateInterval) return;
        baselineUpdateCount = 0;
        baselineBuffer.Add(db);
        if (baselineBuffer.Count > tuning.BaselineWindowSize) baselineBuffer.RemoveAt(0);
        if (baselineBuffer.Count >= 10)
        {
            var quietHalf = baselineBuffer.OrderBy(value => value).Take(baselineBuffer.Count / 2).ToList();
            float total = 0;
            foreach (var value in quietHalf) total += value;
            var newBaseline = total / quietHalf.Count;
            currentBaseline = currentBaseline * 0.7f + newBaseline * 0.3f;
        }
    }

    private List<Emission> ProcessVAD(float db)
    {
        var time = now();
        var emissions = new List<Emission>();
        checkCount++;
        UpdateBaseline(db);
        if (checkCount % 100 == 0)
        {
            Log.Print(string.Create(CultureInfo.InvariantCulture, $"{logTag}: db={db:F1}, baseline={currentBaseline:F1}, speaking={(isSpeaking ? "YES" : "NO")}"));
        }
        if (db > currentBaseline + tuning.SpeechMargin && db > tuning.AbsoluteMinSpeechDb)
        {
            lastSpeechTime = time;
            peakLevel = MathF.Max(peakLevel, db);
            if (!isSpeaking)
            {
                isSpeaking = true;
                speechStartTime = time;
                peakLevel = db;
                recordedSamples = new List<float>();
                didPreviewCurrentUtterance = false;
                emissions.Add(Emission.Status(labels.Speaking));
            }
            else if (didPreviewCurrentUtterance)
            {
                didPreviewCurrentUtterance = false;
                emissions.Add(Emission.Turn(SpeechTurnEmit.SpeechResumedAfterPreview, null));
            }
        }
        else if (isSpeaking)
        {
            var nearBaseline = db < currentBaseline + tuning.SilenceMargin;
            var silence = lastSpeechTime is { } last ? (time - last).TotalSeconds : 0;
            if (nearBaseline && silence > tuning.SpeculativeSilenceTimeout && !didPreviewCurrentUtterance
                && EligibleUtteranceWAV() is { } preview)
            {
                didPreviewCurrentUtterance = true;
                Log.Print(string.Create(CultureInfo.InvariantCulture, $"{logTag}: STT preview {SpeechSeconds():F2}s ({preview.Length} bytes wav)"));
                emissions.Add(Emission.Turn(SpeechTurnEmit.SpeculativePreview, preview));
            }
            if (nearBaseline && silence > tuning.SilenceTimeout)
            {
                if (EligibleUtteranceWAV() is { } audio)
                {
                    Log.Print(string.Create(CultureInfo.InvariantCulture, $"{logTag}: final commit {SpeechSeconds():F2}s ({audio.Length} bytes wav)"));
                    emissions.Add(Emission.Status(labels.Processing));
                    emissions.Add(Emission.Turn(SpeechTurnEmit.FinalSilence, audio));
                }
                else if (didPreviewCurrentUtterance)
                {
                    emissions.Add(Emission.Turn(SpeechTurnEmit.SpeechResumedAfterPreview, null));
                }
                AbandonCurrentUtterance();
                emissions.Add(Emission.Status(labels.Idle));
            }
        }
        var stateChanged = isSpeaking != lastLevelCallbackSpeaking;
        if (stateChanged || lastLevelCallbackTime == DateTime.MinValue
            || (time - lastLevelCallbackTime).TotalSeconds >= tuning.LevelCallbackInterval)
        {
            lastLevelCallbackTime = time;
            lastLevelCallbackSpeaking = isSpeaking;
            emissions.Add(Emission.Level(db, isSpeaking));
        }
        return emissions;
    }

    private double SpeechSeconds() => speechStartTime is { } start ? (now() - start).TotalSeconds : 0;

    private void AbandonCurrentUtterance()
    {
        isSpeaking = false;
        speechStartTime = null;
        lastSpeechTime = null;
        peakLevel = -100;
        recordedSamples.Clear();
        didPreviewCurrentUtterance = false;
    }

    private byte[]? EligibleUtteranceWAV()
    {
        var duration = SpeechSeconds();
        if (duration <= tuning.MinSpeechDuration
            || peakLevel - currentBaseline < tuning.SpeechMargin * 0.5f
            || recordedSamples.Count == 0)
        {
            return null;
        }
        var dataSize = (uint)(recordedSamples.Count * 2);
        var rate = (uint)sampleRate;
        using var stream = new MemoryStream(44 + recordedSamples.Count * 2);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(dataSize + 36);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write((uint)16);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        foreach (var sample in recordedSamples)
        {
            writer.Write((short)(MathF.Max(-1, MathF.Min(1, sample)) * short.MaxValue));
        }
        writer.Flush();
        return stream.ToArray();
    }
}
