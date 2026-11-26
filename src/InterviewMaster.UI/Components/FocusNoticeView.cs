using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// One line of feedback in the header: what happened, and the one thing to do about it.
public sealed class FocusNoticeView : FrameView
{
    private const double IconSize = 14;
    private readonly Border pill = new();
    private readonly IconView icon = new() { Size = IconSize };
    private readonly TextBlock message = Ui.Label("", Token.Font.Caption, Token.Font.Semibold, digits: true);
    private readonly HoverButton actionButton = new() { CornerRadius = new CornerRadius(Token.Radius.Full(Token.Sizing.Badge)) };
    private readonly HoverButton dismissButton = HoverButton.Glyph(Icons.Close, "Dismiss", Token.Sizing.BadgeSmall, 10);

    public event Action? Action;
    public event Action? Dismiss;

    public FocusNoticeView()
    {
        IsVisible = false;
        message.VerticalAlignment = VerticalAlignment.Center;
        actionButton.Click += () => Action?.Invoke();
        dismissButton.Click += () => Dismiss?.Invoke();
        Children.Add(pill);
        Children.Add(icon);
        Children.Add(message);
        Children.Add(actionButton);
        Children.Add(dismissButton);
        Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(Token.Motion.FadeIn) } };
    }

    public FeedbackNotice? Notice { get; private set; }

    public bool IsShowing => Notice is not null;

    public string? ActionTitle => actionButton.IsVisible ? actionButton.Title : null;

    public bool CanDismiss => dismissButton.IsVisible;

    public void Show(FeedbackNotice? next)
    {
        var appearing = Notice is null && next is not null;
        Notice = next;
        if (next is null)
        {
            IsVisible = false;
            return;
        }
        var hue = HueOf(next.Kind);
        var text = next.Kind == FeedbackKind.Info ? Token.Colors.TextBody : hue.Text;
        Ui.Surface(pill, Token.SurfaceStyle.Notice(hue));
        icon.Shape = IconOf(next.Kind);
        icon.Foreground = text.Brush();
        Ui.SetText(message, next.Message);
        message.Foreground = text.Brush();
        Ui.Tip(message, next.Detail ?? next.Message);
        Ui.Tip(pill, next.Detail);
        actionButton.IsVisible = next.ActionTitle is not null;
        if (next.ActionTitle is { } title)
        {
            actionButton.Title = title;
            actionButton.TitleColor = text;
            actionButton.Apply(Token.ControlStyle.NoticeAction(hue));
            AutomationProperties.SetName(actionButton, title);
            actionButton.Help = next.Detail;
        }
        // A notice that does not go away on its own needs a way out.
        dismissButton.IsVisible = next.Lasts is null;
        AutomationProperties.SetName(this, string.Join(". ", new[] { next.Message, next.Detail }.Where(part => part is not null)));
        IsVisible = true;
        if (appearing && !Token.Motion.ReducesMotion())
        {
            var transitions = Transitions;
            Transitions = null;
            Opacity = 0;
            Transitions = transitions;
            Opacity = 1;
        }
        Relayout();
    }

    public static Token.Hue HueOf(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Success => Token.Hue.Success,
        FeedbackKind.Warning => Token.Hue.Warning,
        FeedbackKind.Error => Token.Hue.Danger,
        _ => Token.Hue.Neutral
    };

    public static IconShape IconOf(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Success => Icons.Success,
        FeedbackKind.Warning => Icons.Warning,
        FeedbackKind.Error => Icons.Error,
        _ => Icons.Info
    };

    protected override void LayoutFrames(Size size)
    {
        if (Notice is null) return;
        var height = Token.Sizing.Notice;
        var top = (size.Height - height) / 2;
        var edge = Token.Space.Md;
        var messageX = edge + IconSize + Token.Space.Sm;
        var trailing = edge;
        var actionWidth = 0.0;
        if (actionButton.IsVisible)
        {
            actionWidth = Ui.TextWidth(actionButton.Title, Token.Font.Caption, Token.Font.Semibold) + 20;
            trailing += actionWidth + Token.Space.Sm;
        }
        var dismissSize = Token.Sizing.BadgeSmall;
        if (dismissButton.IsVisible) trailing += dismissSize + Token.Space.Xs;
        var wanted = WidthOf(message) + 4;
        var messageWidth = Math.Max(40, Math.Min(wanted, size.Width - messageX - trailing));
        var pillWidth = Math.Min(size.Width, messageX + messageWidth + trailing);
        Place(pill, 0, top, pillWidth, height);
        Place(icon, edge, top + (height - IconSize) / 2, IconSize, IconSize);
        Place(message, messageX, top + (height - 18) / 2, messageWidth, 18);
        var x = messageX + messageWidth + Token.Space.Sm;
        if (actionButton.IsVisible) Place(actionButton, x, top + (height - Token.Sizing.Badge) / 2, actionWidth, Token.Sizing.Badge);
        if (dismissButton.IsVisible)
        {
            Place(dismissButton, pillWidth - edge - dismissSize + Token.Space.Xs, top + (height - dismissSize) / 2, dismissSize, dismissSize);
        }
    }
}
