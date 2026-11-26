using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// What the CV says about the topic in play: one line of digest and a few points to mention. It
/// gives up lines before it takes room from the answer.
public sealed class BriefingView : FrameView
{
    private const int DigestLines = 2;
    private const int PointLines = 6;
    private const double RowGap = 8;
    private const double BadgeWidth = 28;
    private static readonly double TopPadding = Token.Space.Md;
    private static readonly double BottomPadding = Token.Space.Md;
    private static readonly double PointPitch = Math.Round(Token.Font.Body * Token.Leading.Compact, 1);
    /// The air under a line of text, which the last line of a block does not need.
    private static readonly double LineGap = PointPitch - Math.Ceiling(Token.Font.Body * 1.2);

    private readonly Border surface = new();
    private readonly TextBlock badge = Ui.Overline("CV", Token.Colors.TextMuted);
    private readonly TextBlock digest = Ui.Wrapping("", Token.Font.Body, Token.Font.Medium, Token.Colors.TextStrong, Token.Leading.Compact, DigestLines);
    private readonly List<(TextBlock Mark, TextBlock Text, string Point)> points = new();
    private int shownPoints;

    public BriefingView()
    {
        IsVisible = false;
        badge.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(surface);
        Children.Add(badge);
        Children.Add(digest);
        ApplyTransparency();
    }

    public InterviewBriefing? Briefing { get; private set; }

    public string Digest => digest.Text ?? "";

    /// The points in view after the last layout.
    public IReadOnlyList<string> VisiblePoints => points.Take(shownPoints).Select(point => point.Point).ToList();

    /// The points that did not fit under the digest.
    public IReadOnlyList<string> HiddenPoints => points.Skip(shownPoints).Select(point => point.Point).ToList();

    public void ApplyTransparency() => Ui.Surface(surface, Token.SurfaceStyle.Briefing);

    public void Update(InterviewBriefing? briefing)
    {
        foreach (var (mark, text, _) in points)
        {
            Children.Remove(mark);
            Children.Remove(text);
        }
        points.Clear();
        Briefing = briefing;
        if (briefing is null || briefing.TalkingPoints.Count == 0)
        {
            IsVisible = false;
            Ui.SetText(digest, "");
            return;
        }
        IsVisible = true;
        Ui.SetText(digest, briefing.Digest);
        Ui.Tip(digest, briefing.Digest);
        foreach (var point in briefing.TalkingPoints)
        {
            var mark = Ui.Label("•", Token.Font.Body, color: Token.Colors.TextBody);
            mark.LineHeight = PointPitch;
            mark.Padding = new Thickness(Token.Text.LabelInset, 0, 0, 0);
            var text = Ui.Wrapping(point, Token.Font.Body, color: Token.Colors.TextBody, leading: Token.Leading.Compact);
            text.Padding = new Thickness(0, 0, Token.Text.LabelInset, 0);
            Ui.Tip(text, point);
            points.Add((mark, text, point));
            Children.Add(mark);
            Children.Add(text);
        }
        Ui.Tip(this, string.Join("\n", new[] { briefing.Digest }.Concat(briefing.TalkingPoints.Select(point => "• " + point))));
        Relayout();
    }

    private static double DigestX => Token.Space.Inset + BadgeWidth + Token.Space.Sm;

    private static double BulletIndent => Ui.TextWidth("• ", Token.Font.Body);

    private static double DigestWidth(double width) => Math.Min(Math.Max(40, width - DigestX - Token.Space.Inset), Token.Measure.Caption);

    private static double PointsWidth(double width) => Math.Min(width - Token.Space.Inset * 2, Token.Measure.Caption);

    private double DigestHeight(double width, int lines)
    {
        digest.MaxLines = lines;
        return Math.Max(18, Math.Floor(HeightOf(digest, DigestWidth(width))));
    }

    /// The lines each point takes within `lines` in all, in order, until the lines run out.
    private List<int> PointRows(double width, int lines)
    {
        var rows = new List<int>();
        var left = lines;
        var textWidth = PointsWidth(width) - BulletIndent - Token.Text.LabelInset;
        foreach (var (_, text, _) in points)
        {
            if (left <= 0) break;
            text.MaxLines = left;
            var used = Math.Max(1, (int)Math.Round(HeightOf(text, textWidth) / PointPitch));
            rows.Add(Math.Min(used, left));
            left -= used;
        }
        return rows;
    }

    private double PointsHeight(double width, int lines) => PointRows(width, lines).Sum() * PointPitch;

    /// The height the strip takes at `width`, never more than `budget`.
    public double PreferredHeight(double width, double budget = double.PositiveInfinity)
    {
        if (!IsVisible) return 0;
        var natural = TopPadding + DigestHeight(width, DigestLines) + RowGap + PointsHeight(width, PointLines) + BottomPadding;
        if (natural <= budget) return natural;
        foreach (var digestLines in new[] { DigestLines, 1 })
        {
            var digestHeight = DigestHeight(width, digestLines);
            var room = budget - TopPadding - digestHeight - RowGap - BottomPadding;
            if (room < PointPitch) continue;
            var lines = Math.Min(PointLines, (int)Math.Floor((room + LineGap) / PointPitch));
            return Math.Min(budget, TopPadding + digestHeight + RowGap + PointsHeight(width, lines) + BottomPadding);
        }
        return TopPadding + DigestHeight(width, 1) + BottomPadding;
    }

    protected override void LayoutFrames(Size size)
    {
        Place(surface, 0, 0, size.Width, size.Height);
        var inset = Token.Space.Inset;
        var digestLines = DigestLines;
        var digestHeight = DigestHeight(size.Width, digestLines);
        var room = size.Height - TopPadding - digestHeight - RowGap - BottomPadding;
        if (room < PointPitch)
        {
            digestLines = 1;
            digestHeight = DigestHeight(size.Width, 1);
            room = size.Height - TopPadding - digestHeight - RowGap - BottomPadding;
        }
        var lines = room >= PointPitch ? Math.Max(1, Math.Min(PointLines, (int)Math.Floor((room + LineGap) / PointPitch))) : 0;
        Place(badge, inset, TopPadding, BadgeWidth, 18);
        Place(digest, DigestX, TopPadding, DigestWidth(size.Width), digestHeight);

        var rows = lines == 0 ? new List<int>() : PointRows(size.Width, lines);
        shownPoints = rows.Count;
        var y = TopPadding + digestHeight + RowGap;
        var indent = BulletIndent;
        var textWidth = PointsWidth(size.Width) - indent - Token.Text.LabelInset;
        for (var index = 0; index < points.Count; index++)
        {
            var (mark, text, point) = points[index];
            var visible = index < rows.Count;
            mark.IsVisible = visible;
            text.IsVisible = visible;
            if (!visible) continue;
            // The last point in view says there is more when points were left out.
            var cut = index == rows.Count - 1 && rows.Count < points.Count;
            Ui.SetText(text, cut ? point.TrimEnd('.', ' ') + "…" : point);
            text.MaxLines = rows[index];
            var height = rows[index] * PointPitch;
            Place(mark, inset, y, indent + Token.Text.LabelInset, PointPitch);
            Place(text, inset + indent + Token.Text.LabelInset, y, textWidth, height);
            y += height;
        }
    }
}
