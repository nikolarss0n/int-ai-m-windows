using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace InterviewMaster.Presentation;

/// The title row: the app's mark and the interview's profile centred as one group, the status at the
/// right edge. The group slides left when the status grows, and a notice takes the title's place.
public sealed class FocusHeaderView : FrameView
{
    private readonly AppIconView icon = new();
    private readonly TextBlock title = Ui.Label("", Token.Font.Body, Token.Font.Semibold, Token.Colors.TextPrimary, digits: true);
    private readonly HoverButton closeButton = HoverButton.Glyph(Icons.Close, "Hide window", Token.Sizing.ControlSmall, 12);
    private double reservedTrailingWidth;
    private bool showsWindowButtons = true;

    public FocusHeaderView()
    {
        title.VerticalAlignment = VerticalAlignment.Center;
        Capsule = new ActivityCapsuleView();
        Notice = new FocusNoticeView();
        Children.Add(icon);
        Children.Add(title);
        Children.Add(Notice);
        Children.Add(Capsule);
        Children.Add(closeButton);
        closeButton.Click += () => CloseRequested?.Invoke();
    }

    public event Action? CloseRequested;

    public ActivityCapsuleView Capsule { get; }

    public FocusNoticeView Notice { get; }

    public HoverButton CloseButton => closeButton;

    public string Title => title.Text ?? "";

    /// Room kept free at the right for the status while the interview runs, so the title does not
    /// jump each time the status changes length.
    public double ReservedTrailingWidth
    {
        get => reservedTrailingWidth;
        set
        {
            if (reservedTrailingWidth == value) return;
            reservedTrailingWidth = value;
            Relayout();
        }
    }

    /// Windows has no title bar here, so the header carries the button that puts the window away.
    public bool ShowsWindowButtons
    {
        get => showsWindowButtons;
        set
        {
            showsWindowButtons = value;
            closeButton.IsVisible = value;
            Relayout();
        }
    }

    public void SetTitle(string text, string tip)
    {
        Ui.SetText(title, text);
        Ui.Tip(title, tip);
        Relayout();
    }

    /// The title steps aside while a notice shows.
    public void SetTitleHidden(bool hidden)
    {
        title.IsVisible = !hidden;
        Relayout();
    }

    protected override Size PreferredSize(Size available) => new(available.Width, Token.Header.Height);

    protected override void LayoutFrames(Size size)
    {
        var trailingEdge = size.Width;
        if (closeButton.IsVisible)
        {
            var side = Token.Sizing.ControlSmall;
            Place(closeButton, size.Width - side, (size.Height - side) / 2, side, side);
            trailingEdge = size.Width - side - Token.Space.Sm;
        }
        var capsuleWidth = Capsule.FittingWidth;
        var capsuleX = trailingEdge - capsuleWidth;
        if (Capsule.IsVisible) Place(Capsule, capsuleX, (size.Height - Capsule.Height) / 2, capsuleWidth, Capsule.Height);

        const double left = 0;
        var right = capsuleX - Token.Space.Inset;
        var steadyRight = Math.Min(right, trailingEdge - reservedTrailingWidth - Token.Space.Inset);
        var titleOffset = Token.Header.TitleX;
        var room = Math.Max(0, right - left - titleOffset);
        var iconY = (size.Height - Token.Header.IconSize) / 2;
        if (Notice.IsVisible) Place(Notice, left + titleOffset, 0, room, size.Height);
        if (!title.IsVisible)
        {
            Place(icon, left, iconY, Token.Header.IconSize, Token.Header.IconSize);
            return;
        }
        var titleWidth = Math.Min(WidthOf(title) + 6, room);
        var groupWidth = titleOffset + titleWidth;
        var x = Math.Max(left, Math.Min(size.Width / 2 - groupWidth / 2, steadyRight - groupWidth));
        Place(icon, x, iconY, Token.Header.IconSize, Token.Header.IconSize);
        Place(title, x + titleOffset, (size.Height - 18) / 2, titleWidth, 18);
    }
}
