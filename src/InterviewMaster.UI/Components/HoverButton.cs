using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace InterviewMaster.Presentation;

/// A button that answers the pointer: a fill on hover, a stronger one while pressed, and a ring while
/// it has the keyboard. It holds a glyph, a word, or both.
public class HoverButton : Border
{
    private readonly StackPanel content = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly IconView icon = new() { IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = Ui.Label("", Token.Font.Caption, Token.Font.Semibold);
    private Token.ControlStyle style = Token.ControlStyle.Ghost;
    private Color? glyphOverride;
    private Color titleColor = Token.Colors.TextBody;
    private bool hovered;
    private bool pressed;

    public event Action? Click;

    public HoverButton()
    {
        title.Padding = default;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        title.IsVisible = false;
        title.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(icon);
        content.Children.Add(title);
        Child = content;
        Transitions = new Transitions
        {
            new BrushTransition { Property = BackgroundProperty, Duration = TimeSpan.FromSeconds(0.15), Easing = new CubicEaseOut() }
        };
        RenderTransformOrigin = RelativePoint.Center;
        Apply(Token.ControlStyle.Ghost);
    }

    /// A round glyph button.
    public static HoverButton Glyph(IconShape shape, string label, double side = Token.Sizing.Control, double iconSize = Token.Sizing.Icon, Action? click = null)
    {
        var button = new HoverButton { Width = side, Height = side, CornerRadius = new CornerRadius(Token.Radius.Full(side)) };
        button.Icon = shape;
        button.IconSize = iconSize;
        button.Label = label;
        if (click is not null) button.Click += click;
        return button;
    }

    /// A capsule with a word on it.
    public static HoverButton Chip(string text, Token.ControlStyle style, Color? textColor = null, double height = Token.Sizing.ControlSmall, Action? click = null)
    {
        var button = new HoverButton { Height = height, CornerRadius = new CornerRadius(Token.Radius.Full(height)), Padding = new Thickness(12, 0) };
        button.Title = text;
        button.TitleColor = textColor ?? Token.Hue.Neutral.Text;
        button.Label = text;
        button.Apply(style);
        if (click is not null) button.Click += click;
        return button;
    }

    public IconShape? Icon
    {
        get => icon.Shape;
        set
        {
            icon.Shape = value;
            icon.IsVisible = value is not null;
            RefreshSpacing();
        }
    }

    public double IconSize
    {
        get => icon.Size;
        set => icon.Size = value;
    }

    public string Title
    {
        get => title.Text ?? "";
        set
        {
            title.Text = value;
            title.IsVisible = value.Length > 0;
            RefreshSpacing();
        }
    }

    public double TitleSize
    {
        get => title.FontSize;
        set => title.FontSize = value;
    }

    public FontWeight TitleWeight
    {
        get => title.FontWeight;
        set => title.FontWeight = value;
    }

    public bool TitleDigits
    {
        set => title.FontFeatures = value ? Token.Font.TabularDigits : null;
    }

    public Color TitleColor
    {
        get => titleColor;
        set
        {
            titleColor = value;
            title.Foreground = value.Brush();
        }
    }

    /// The glyph's colour when it is not the style's.
    public Color? GlyphColor
    {
        get => glyphOverride;
        set
        {
            glyphOverride = value;
            RefreshGlyph();
        }
    }

    /// What the button is called by a screen reader and, unless `Help` says more, on hover.
    public string Label
    {
        get => AutomationProperties.GetName(this) ?? "";
        set
        {
            AutomationProperties.SetName(this, value);
            if (ToolTip.GetTip(this) is null) ToolTip.SetTip(this, value);
        }
    }

    public string? Help
    {
        get => ToolTip.GetTip(this) as string;
        set => ToolTip.SetTip(this, value);
    }

    public Token.ControlStyle Style => style;

    public bool IsHovered => hovered;

    public void Apply(Token.ControlStyle next)
    {
        style = next;
        BorderThickness = new Thickness(next.BorderWidth);
        RefreshGlyph();
        ApplyVisualState();
    }

    public void PerformClick()
    {
        if (IsEffectivelyEnabled) Click?.Invoke();
    }

    private void RefreshSpacing() => icon.Margin = new Thickness(0, 0, icon.IsVisible && title.IsVisible ? 4 : 0, 0);

    private void RefreshGlyph() => icon.Foreground = (glyphOverride ?? style.Glyph ?? titleColor).Brush();

    private void ApplyVisualState()
    {
        var active = IsEffectivelyEnabled;
        var background = pressed && active ? style.Press : hovered && active ? style.Hover : style.Background;
        Background = background.Brush();
        BorderBrush = (IsFocused && IsKeyboardFocusWithin ? Token.Colors.FocusRing : hovered && active ? style.HoverBorder : style.Border).Brush();
        if (IsFocused && IsKeyboardFocusWithin) BorderThickness = new Thickness(2);
        else BorderThickness = new Thickness(style.BorderWidth);
        var scale = Token.Motion.ReducesMotion() || !active ? 1 : pressed ? 0.97 : hovered ? 1.02 : 1;
        RenderTransform = scale == 1 ? null : new ScaleTransform(scale, scale);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        hovered = true;
        ApplyVisualState();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hovered = false;
        pressed = false;
        ApplyVisualState();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressed = true;
        ApplyVisualState();
        // The window is dragged by its background; a press on a button is not a drag.
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!pressed) return;
        pressed = false;
        ApplyVisualState();
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) PerformClick();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Enter or Key.Space)) return;
        PerformClick();
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        ApplyVisualState();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        ApplyVisualState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) ApplyVisualState();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new HoverButtonPeer(this);

    private sealed class HoverButtonPeer(HoverButton owner) : ControlAutomationPeer(owner), Avalonia.Automation.Provider.IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        public void Invoke() => owner.PerformClick();
    }
}
