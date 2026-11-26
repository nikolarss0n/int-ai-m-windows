using System.Buffers.Binary;

namespace InterviewMaster.Infrastructure;

public enum PcmSampleKind
{
    Float32,
    Int16,
    Int24,
    Int32
}

/// Interleaved little-endian PCM, as an audio device delivers it.
public readonly record struct PcmFormat(int SampleRate, int Channels, PcmSampleKind Kind)
{
    public int BytesPerSample => Kind switch
    {
        PcmSampleKind.Int16 => 2,
        PcmSampleKind.Int24 => 3,
        _ => 4
    };

    public int BytesPerFrame => BytesPerSample * Channels;

    /// What the speech detector and the transcription WAVs use.
    public static readonly PcmFormat Mono48k = new(PcmConverter.OutputSampleRate, 1, PcmSampleKind.Float32);
}

public sealed class CaptureFormatException(string message) : Exception(message);

/// Turns device buffers into 48 kHz mono float samples. The macOS app asks `AVAudioConverter` for the
/// same output; Windows is asked to deliver it directly, and this covers a device that does not.
public sealed class PcmConverter
{
    public const int OutputSampleRate = 48_000;

    private readonly PcmFormat input;
    private readonly double step;
    private readonly int smoothing;
    private readonly float[] window;
    private int windowIndex;
    private float windowSum;
    private double position;
    private float last;
    private float[] mono = Array.Empty<float>();
    private float[] output = Array.Empty<float>();

    public PcmConverter(PcmFormat input)
    {
        if (input.SampleRate <= 0 || input.Channels <= 0)
        {
            throw new CaptureFormatException("The audio device has no usable audio format.");
        }
        this.input = input;
        step = (double)input.SampleRate / OutputSampleRate;
        // Averaging over the samples each output replaces keeps a faster device from aliasing.
        smoothing = step >= 1.5 ? (int)Math.Round(step) : 1;
        window = new float[smoothing];
    }

    public PcmFormat Input => input;

    public bool IsPassthrough => input == PcmFormat.Mono48k;

    /// The returned samples are valid until the next call.
    public ReadOnlySpan<float> Convert(ReadOnlySpan<byte> interleaved)
    {
        var frames = interleaved.Length / input.BytesPerFrame;
        if (frames == 0) return ReadOnlySpan<float>.Empty;
        if (mono.Length < frames) mono = new float[frames];
        Downmix(interleaved, frames);
        if (input.SampleRate == OutputSampleRate) return mono.AsSpan(0, frames);

        if (smoothing > 1)
        {
            for (var index = 0; index < frames; index++)
            {
                windowSum += mono[index] - window[windowIndex];
                window[windowIndex] = mono[index];
                windowIndex = (windowIndex + 1) % smoothing;
                mono[index] = windowSum / smoothing;
            }
        }

        var capacity = (int)Math.Ceiling(frames / step) + 2;
        if (output.Length < capacity) output = new float[capacity];
        var count = 0;
        while (true)
        {
            var index = (int)Math.Floor(position);
            if (index + 1 >= frames) break;
            var from = index < 0 ? last : mono[index];
            output[count++] = from + (mono[index + 1] - from) * (float)(position - index);
            position += step;
        }
        position -= frames;
        last = mono[frames - 1];
        return output.AsSpan(0, count);
    }

    private void Downmix(ReadOnlySpan<byte> interleaved, int frames)
    {
        var channels = input.Channels;
        var size = input.BytesPerSample;
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                var sample = interleaved.Slice((frame * channels + channel) * size, size);
                sum += input.Kind switch
                {
                    PcmSampleKind.Float32 => BinaryPrimitives.ReadSingleLittleEndian(sample),
                    PcmSampleKind.Int16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
                    PcmSampleKind.Int24 => ((sample[0] << 8 | sample[1] << 16 | sample[2] << 24) >> 8) / 8388608f,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f
                };
            }
            var value = sum / channels;
            mono[frame] = float.IsFinite(value) ? value : 0;
        }
    }
}
