using System.Buffers.Binary;
using System.IO.Compression;
using InterviewMaster.Domain;

namespace InterviewMaster.Infrastructure;

public enum ScreenCaptureFailure
{
    NoDisplayFound,
    CaptureFailed
}

public sealed class ScreenCaptureException(ScreenCaptureFailure failure, string message) : Exception(message)
{
    public ScreenCaptureFailure Failure { get; } = failure;
}

/// Takes a picture of the main display without the app's own windows in it. The platform supplies it.
public interface IScreenCapture
{
    /// Throws `ScreenCaptureException`.
    Task<Screenshot> CaptureScreenAsync();
}

/// Writes screen pixels as a PNG, the format the answer models are sent. Screens have no
/// transparency, so the alpha channel is dropped.
public static class PngEncoder
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// `bgra` holds rows from the top, four bytes a pixel, `stride` bytes a row.
    public static byte[] EncodeBgra(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || bgra.Length < (long)stride * (height - 1) + width * 4)
        {
            throw new ArgumentException("The pixel buffer does not match the image size.");
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; // bits per channel
        header[9] = 2; // truecolour
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + width * 3];
            for (var y = 0; y < height; y++)
            {
                var source = bgra.Slice(y * stride, width * 4);
                for (var x = 0; x < width; x++)
                {
                    row[1 + x * 3] = source[x * 4 + 2];
                    row[2 + x * 3] = source[x * 4 + 1];
                    row[3 + x * 3] = source[x * 4];
                }
                zlib.Write(row);
            }
        }
        WriteChunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        Span<byte> name = stackalloc byte[4];
        for (var index = 0; index < 4; index++) name[index] = (byte)type[index];
        output.Write(name);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc(data, Crc(name, 0xFFFFFFFF)) ^ 0xFFFFFFFF);
        output.Write(word);
    }

    private static uint Crc(ReadOnlySpan<byte> data, uint crc)
    {
        foreach (var value in data) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < 256; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }
}
