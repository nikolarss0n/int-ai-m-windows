using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace InterviewMaster.Presentation;

/// The app's mark: a pair of closing quotation marks, one white and one in the accent, on a dark tile.
public sealed class AppIconView : Control
{
    private const double Grid = 22;

    public AppIconView()
    {
        IsHitTestVisible = false;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Token.Header.IconSize, Token.Header.IconSize);

    public override void Render(DrawingContext context) => Draw(context, Math.Min(Bounds.Width, Bounds.Height));

    /// Draws the mark `side` points wide at the origin. Also used to make the app's icon file.
    public static void Draw(DrawingContext context, double side)
    {
        using var scale = context.PushTransform(Matrix.CreateScale(side / Grid, side / Grid));
        context.DrawRectangle(Token.Palette.Charcoal.Brush(), new Pen(Token.Colors.BorderSubtle.Brush(), 0.6), new Rect(0.3, 0.3, Grid - 0.6, Grid - 0.6), 5.2, 5.2);
        DrawMark(context, Token.Palette.White.Brush(), 7.5);
        DrawMark(context, Token.Palette.Magenta.Brush(), 14.5);
    }

    /// One closing quotation mark: a dot and the tail that hangs from its right side. They are drawn
    /// one after the other, because a single path would be left hollow where the two overlap.
    private static void DrawMark(DrawingContext context, IBrush brush, double cx)
    {
        const double cy = 9.6, r = 2.75;
        context.DrawEllipse(brush, null, new Point(cx, cy), r, r);
        FormattableString tail = $"M{cx + r} {cy}C{cx + r} {cy + 3.4} {cx + 0.7} {cy + 5.6} {cx - 1.9} {cy + 6.1}L{cx - 2.2} {cy + 4.7}C{cx - 0.6} {cy + 4.3} {cx + 0.2} {cy + 3.3} {cx + 0.1} {cy + 2.2}z";
        context.DrawGeometry(brush, null, Geometry.Parse(FormattableString.Invariant(tail)));
    }
}
