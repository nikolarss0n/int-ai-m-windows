using System.Buffers.Binary;

namespace InterviewMaster.Infrastructure;

/// Records a spoken practice answer from the microphone. Windows supplies the real one; without one
/// the practice tab asks for a typed answer.
public interface IPracticeRecorder
{
    /// Asks for the microphone. False when the person or a policy refuses it.
    Task<bool> RequestAccessAsync();

    /// Starts a new recording. False when the microphone cannot be opened.
    Task<bool> StartAsync();

    /// Stops recording. What was recorded stays until the next start or `Discard`.
    void Stop();

    /// The last recording as a file the transcriber accepts, or null when there is none.
    byte[]? Recording { get; }

    void Discard();
}

/// Records from an audio device into a 16 kHz mono WAV, the size and shape the macOS app records
/// (16 kHz mono) and the transcriber takes without converting.
public sealed class PcmPracticeRecorder : IPracticeRecorder
{
    public const int SampleRate = 16_000;

    /// The transcriber refuses files over 25 MB; this is about twelve minutes of speech.
    public const int MaximumBytes = 24_000_000;

    private const int Decimation = PcmConverter.OutputSampleRate / SampleRate;

    private readonly object gate = new();
    private readonly Func<IPcmSource> open;
    private readonly Func<Task> preflight;
    private IPcmSource? source;
    private PcmConverter? converter;
    private MemoryStream samples = new();
    private float carry;
    private int carried;

    public PcmPracticeRecorder(Func<IPcmSource> open, Func<Task> preflight)
    {
        this.open = open;
        this.preflight = preflight;
    }

    public async Task<bool> RequestAccessAsync()
    {
        try
        {
            await preflight().ConfigureAwait(false);
            return true;
        }
        catch (AudioCapturePermissionException)
        {
            return false;
        }
    }

    public async Task<bool> StartAsync()
    {
        Stop();
        Discard();
        IPcmSource opened;
        try
        {
            opened = open();
        }
        catch (Exception)
        {
            return false;
        }
        lock (gate)
        {
            source = opened;
            converter = null;
        }
        try
        {
            await opened.StartAsync((interleaved, format) => Consume(opened, interleaved, format), _ => Ended(opened)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Ended(opened);
            return false;
        }
        lock (gate) return ReferenceEquals(source, opened);
    }

    public void Stop()
    {
        IPcmSource? old;
        lock (gate)
        {
            old = source;
            source = null;
        }
        try
        {
            old?.StopSync();
        }
        catch (Exception)
        {
        }
    }

    public byte[]? Recording
    {
        get
        {
            lock (gate)
            {
                return samples.Length == 0 ? null : Wav(samples.GetBuffer().AsSpan(0, (int)samples.Length));
            }
        }
    }

    public void Discard()
    {
        lock (gate)
        {
            samples = new MemoryStream();
            carry = 0;
            carried = 0;
        }
    }

    private void Ended(IPcmSource from)
    {
        lock (gate)
        {
            if (ReferenceEquals(source, from)) source = null;
        }
    }

    private void Consume(IPcmSource from, ReadOnlySpan<byte> interleaved, PcmFormat format)
    {
        lock (gate)
        {
            if (!ReferenceEquals(source, from) || samples.Length >= MaximumBytes) return;
            try
            {
                if (converter is null || converter.Input != format) converter = new PcmConverter(format);
                Span<byte> sample = stackalloc byte[2];
                // Three 48 kHz samples make one at 16 kHz; their mean keeps what is above 8 kHz out.
                foreach (var value in converter.Convert(interleaved))
                {
                    carry += value;
                    carried++;
                    if (carried < Decimation) continue;
                    var mean = Math.Clamp(carry / Decimation, -1f, 1f);
                    carry = 0;
                    carried = 0;
                    BinaryPrimitives.WriteInt16LittleEndian(sample, (short)Math.Round(mean * short.MaxValue));
                    samples.Write(sample);
                }
            }
            catch (CaptureFormatException)
            {
                source = null;
            }
        }
    }

    /// A 16-bit mono WAV around the samples.
    public static byte[] Wav(ReadOnlySpan<byte> pcm, int sampleRate = SampleRate)
    {
        var file = new byte[44 + pcm.Length];
        var span = file.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + pcm.Length);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], pcm.Length);
        pcm.CopyTo(span[44..]);
        return file;
    }
}
