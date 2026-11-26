using System.Buffers.Binary;
using System.IO.Compression;
using InterviewMaster.Infrastructure;

namespace InterviewMaster.Tests;

public sealed class PngEncoderTests : SwiftStyleTests
{
    [Fact]
    public void ScreenPixelsSurviveTheRoundTrip()
    {
        const int width = 5, height = 3, stride = width * 4 + 8;
        var bgra = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bgra[y * stride + x * 4] = (byte)(10 * x);
                bgra[y * stride + x * 4 + 1] = (byte)(100 + y);
                bgra[y * stride + x * 4 + 2] = (byte)(200 + x + y);
                bgra[y * stride + x * 4 + 3] = 0;
            }
        }
        var png = PngEncoder.EncodeBgra(bgra, width, height, stride);

        Check(png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "PNG signature");
        var offset = 8;
        var chunks = new List<(string Type, byte[] Data)>();
        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length).ToArray();
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));
            Check(crc == ReferenceCrc(png.AsSpan(offset + 4, 4 + length)), $"{type} checksum");
            chunks.Add((type, data));
            offset += 12 + length;
        }
        Check(chunks.Select(chunk => chunk.Type).SequenceEqual(new[] { "IHDR", "IDAT", "IEND" }), "header, pixels, end");
        var header = chunks[0].Data;
        Check(BinaryPrimitives.ReadInt32BigEndian(header) == width && BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)) == height, "size");
        Check(header[8] == 8 && header[9] == 2 && header[10] == 0 && header[11] == 0 && header[12] == 0, "8-bit RGB, no interlace");

        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress)) zlib.CopyTo(inflated);
        var rows = inflated.ToArray();
        Check(rows.Length == height * (1 + width * 3), "one filter byte and three bytes a pixel per row");
        var same = true;
        for (var y = 0; y < height; y++)
        {
            same &= rows[y * (1 + width * 3)] == 0;
            for (var x = 0; x < width; x++)
            {
                var pixel = rows.AsSpan(y * (1 + width * 3) + 1 + x * 3, 3);
                same &= pixel[0] == (byte)(200 + x + y) && pixel[1] == (byte)(100 + y) && pixel[2] == (byte)(10 * x);
            }
        }
        Check(same, "red, green and blue land where a PNG reader expects them");
    }

    [Fact]
    public void WrongBufferSizeIsRejected()
    {
        var threw = false;
        try { PngEncoder.EncodeBgra(new byte[10], 4, 4, 16); }
        catch (ArgumentException) { threw = true; }
        Check(threw, "a short buffer is reported, not read past");
    }

    private static uint ReferenceCrc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
