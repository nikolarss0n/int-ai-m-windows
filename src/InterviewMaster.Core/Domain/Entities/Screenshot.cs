namespace InterviewMaster.Domain;

/// Domain Entity: Screenshot
/// Represents a captured screenshot with metadata. The image is kept as PNG bytes, so this layer needs
/// no drawing library; the interface decodes and scales it.
public sealed class Screenshot : IEquatable<Screenshot>
{
    public Guid Id { get; }
    public byte[] PngData { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public DateTime CapturedAt { get; }

    public Screenshot(byte[] pngData, int pixelWidth, int pixelHeight, Guid? id = null, DateTime? capturedAt = null)
    {
        Id = id ?? Guid.NewGuid();
        PngData = pngData;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        CapturedAt = capturedAt ?? DateTime.Now;
    }

    /// The size a thumbnail takes inside a box, keeping the screenshot's shape.
    public (double Width, double Height) ThumbnailSize(double width, double height)
    {
        var aspectRatio = (double)PixelWidth / PixelHeight;
        return aspectRatio > 1 ? (width, width / aspectRatio) : (height * aspectRatio, height);
    }

    /// Convert image to base64 encoded PNG string
    public string? ToBase64() => PngData.Length == 0 ? null : Convert.ToBase64String(PngData);

    public bool Equals(Screenshot? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is Screenshot other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
