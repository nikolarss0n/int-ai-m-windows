using Avalonia;
using Avalonia.Media;

namespace InterviewMaster.Presentation;

/// Bars standing where text will be, with a highlight passing over them while the wait lasts.
public sealed class SkeletonView : AnimatedControl
{
    /// `Share` is a share of the width right of `X`; otherwise `Width` is in points.
    public readonly record struct Piece(double X, double Top, double Width, double Height, bool Share = false);

    private IReadOnlyList<Piece> pieces = Array.Empty<Piece>();

    public SkeletonView()
    {
        IsHitTestVisible = false;
    }

    public IReadOnlyList<Piece> Pieces
    {
        get => pieces;
        set
        {
            if (pieces.SequenceEqual(value)) return;
            pieces = value;
            InvalidateVisual();
        }
    }

    protected override bool WantsFrames => pieces.Count > 0;

    public Rect FrameOf(Piece piece)
    {
        var width = piece.Share ? Math.Max(0, Bounds.Width - piece.X) * piece.Width : piece.Width;
        return new Rect(piece.X, piece.Top, width, piece.Height);
    }

    public override void Render(DrawingContext context)
    {
        var bar = Token.Colors.BgSkeleton.Brush();
        IBrush? sweep = null;
        if (!Token.Motion.ReducesMotion() && Bounds.Width > 0)
        {
            var band = Math.Max(80, Bounds.Width * 0.5);
            var travelled = FrameClock.Now() % Token.Motion.Shimmer / Token.Motion.Shimmer * (Bounds.Width + band);
            sweep = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(travelled - band, 0, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(travelled, 0, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(Token.Colors.BgSkeletonSweepClear, 0),
                    new GradientStop(Token.Colors.BgSkeletonSweep, 0.5),
                    new GradientStop(Token.Colors.BgSkeletonSweepClear, 1)
                }
            };
        }
        foreach (var piece in pieces)
        {
            var rect = FrameOf(piece);
            var radius = Token.Radius.Full(Math.Min(rect.Width, rect.Height));
            context.DrawRectangle(bar, null, rect, radius, radius);
            if (sweep is not null) context.DrawRectangle(sweep, null, rect, radius, radius);
        }
    }

    /// The rows of an answer on its way: a bullet dot and a line of uneven length.
    public static List<Piece> AnswerLines(int count, double indent, double pitch, double lineHeight)
    {
        double[] shares = { 0.94, 0.8, 0.58, 0.72, 0.5 };
        var bar = Token.Skeleton.LineHeight;
        var dot = Token.Skeleton.Dot;
        var result = new List<Piece>();
        for (var row = 0; row < Math.Max(0, count); row++)
        {
            var centre = lineHeight / 2 + row * pitch;
            result.Add(new Piece(1, centre - dot / 2, dot, dot));
            result.Add(new Piece(indent, centre - bar / 2, shares[row % shares.Length], bar, Share: true));
        }
        return result;
    }
}
