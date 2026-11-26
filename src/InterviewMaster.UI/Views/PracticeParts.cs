using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace InterviewMaster.Presentation;

/// The looks the practice tab's parts share.
internal static class PracticeUi
{
    public static readonly Color Blue = Token.Palette.Blue;

    /// A practice button: a tint of what it does, with a rim of the same colour.
    public static Token.ControlStyle Accent(Color accent, double border = 1.4) => new(
        null, accent.WithAlpha(0.15), accent.WithAlpha(0.25), accent.WithAlpha(0.35), accent.WithAlpha(0.3), accent.WithAlpha(0.5), border);

    public static HoverButton Button(string title, Color accent, IconShape? icon = null)
    {
        var button = new HoverButton { CornerRadius = new CornerRadius(8) };
        button.TitleSize = 12;
        button.TitleWeight = Token.Font.Semibold;
        button.TitleColor = Token.Palette.White;
        button.Apply(Accent(accent));
        if (icon is not null)
        {
            button.Icon = icon;
            button.IconSize = 14;
        }
        SetTitle(button, title);
        return button;
    }

    public static void SetTitle(HoverButton button, string title)
    {
        button.Title = title;
        AutomationProperties.SetName(button, title);
        ToolTip.SetTip(button, null);
    }

    /// A button that cannot act now stays in place and steps back.
    public static void Enable(Control control, bool on)
    {
        control.IsEnabled = on;
        control.Opacity = on ? 1 : Token.State.DisabledAlpha;
    }

    /// How tall a text is when it wraps at a width, however many lines its label shows.
    public static double TextHeight(string text, double size, FontWeight? weight, double width, double minimum = 18)
    {
        if (width <= 1 || text.Length == 0) return minimum;
        var formatted = Ui.Formatted(text, size, Token.Palette.White, weight);
        formatted.MaxTextWidth = width;
        return Math.Max(minimum, Math.Ceiling(formatted.Height));
    }

    /// A label that wraps inside its frame and is cut where the frame ends, as an AppKit label is.
    public static TextBlock Label(string text, double size, FontWeight? weight = null, Color? color = null, int maxLines = 2)
    {
        var label = Ui.Label(text, size, weight, color);
        label.TextWrapping = TextWrapping.Wrap;
        label.TextTrimming = TextTrimming.None;
        label.MaxLines = maxLines;
        label.ClipToBounds = true;
        return label;
    }

    /// Text the person can select and copy, in a scroller of its own.
    public static (ScrollViewer Scroll, SelectableTextBlock Text) Reading(double size, Color color, FontWeight? weight = null, Thickness inset = default)
    {
        var text = new SelectableTextBlock
        {
            FontSize = size,
            FontWeight = weight ?? Token.Font.Regular,
            FontFamily = Token.Font.Family,
            Foreground = color.Brush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = inset,
            SelectionBrush = Token.Selection.Wash.Brush(),
        };
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = text };
        return (scroll, text);
    }

    /// A plain writing area with no box of its own: the card draws what is around it.
    public static TextBox Editor(double size, string name, Thickness inset = default)
    {
        var editor = Ui.Named(new TextBox
        {
            FontSize = size,
            FontFamily = Token.Font.Family,
            Foreground = Token.Palette.White.Brush(),
            CaretBrush = Token.Palette.White.Brush(),
            SelectionBrush = Token.Selection.Wash.Brush(),
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            NewLine = "\n",
            Padding = inset,
            MinHeight = 0,
            BorderThickness = default,
            CornerRadius = default,
            VerticalContentAlignment = VerticalAlignment.Top,
        }, name);
        var clear = Token.Colors.Clear.Brush();
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled" })
        {
            editor.Resources[key] = clear;
        }
        var ink = Token.Palette.White.WithAlpha(0.94).Brush();
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused", "TextControlForegroundDisabled" })
        {
            editor.Resources[key] = ink;
        }
        editor.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        return editor;
    }

    public static Border Card(Color fill, Color? rim = null, double radius = 14) => new()
    {
        Background = fill.Brush(),
        BorderBrush = (rim ?? Token.Colors.Clear).Brush(),
        BorderThickness = new Thickness(rim is null ? 0 : 1),
        CornerRadius = new CornerRadius(radius),
    };

    /// The round letter that says what the card holds: Q for a question, T for today, R for a report.
    public static (Border Badge, TextBlock Letter) Badge(double fillAlpha)
    {
        var letter = Ui.Label("Q", 12, FontWeight.Bold, Blue);
        letter.Padding = default;
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        letter.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = Blue.WithAlpha(fillAlpha).Brush(),
            BorderBrush = Blue.WithAlpha(0.55).Brush(),
            BorderThickness = new Thickness(1),
            Child = letter,
        };
        return (badge, letter);
    }

    public static IconView Symbol(IconShape shape, Color color) => new() { Shape = shape, Size = 16, Foreground = color.Brush() };

    public static Border Rule() => new() { Background = Token.Palette.White.WithAlpha(0.14).Brush() };
}

/// A tick box with its words, drawn here so it looks the same on every Windows version.
public sealed class PracticeCheckBox : Border
{
    private readonly Border box = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(3.5), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    private readonly IconView tick = new() { Shape = Icons.Checkmark, Size = 10, Foreground = Token.Palette.White.Brush(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock label;
    private bool isChecked;

    public event Action? Toggled;

    public PracticeCheckBox(string title, double size, string? id = null)
    {
        Id = id ?? title;
        label = Ui.Label(title, size, Token.Font.Medium, Token.Palette.White);
        label.Padding = default;
        label.Margin = new Thickness(6, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        box.Child = tick;
        Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { box, label } };
        Background = Token.Colors.Clear.Brush();
        Padding = new Thickness(2, 0);
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, title);
        Refresh();
    }

    public string Id { get; }

    public string Title => label.Text ?? "";

    public bool IsChecked
    {
        get => isChecked;
        set
        {
            isChecked = value;
            Refresh();
        }
    }

    /// The width the box and its words need, as a Mac checkbox reports it.
    public double NaturalWidth => Math.Ceiling(23 + Ui.TextWidth(Title, label.FontSize, Token.Font.Medium));

    public void PerformClick()
    {
        if (!IsEffectivelyEnabled) return;
        isChecked = !isChecked;
        Refresh();
        Toggled?.Invoke();
    }

    private void Refresh()
    {
        box.Background = (isChecked ? PracticeUi.Blue : Token.Palette.White.WithAlpha(0.08)).Brush();
        box.BorderBrush = (isChecked ? PracticeUi.Blue : Token.Palette.White.WithAlpha(0.35)).Brush();
        tick.IsVisible = isChecked;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        PerformClick();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Space) return;
        PerformClick();
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) Opacity = IsEffectivelyEnabled ? 1 : Token.State.DisabledAlpha;
    }
}

/// A date with a stepper: the day of the interview the reviews are planned towards.
public sealed class PracticeDateStepper : FrameView
{
    private readonly Border well = new() { CornerRadius = new CornerRadius(5), Background = Token.Palette.White.WithAlpha(0.1).Brush() };
    private readonly TextBlock text = Ui.Label("", 12, color: Token.Palette.White, digits: true);
    private readonly HoverButton earlier = HoverButton.Glyph(Icons.ChevronDown, "A day earlier", side: 12, iconSize: 8);
    private readonly HoverButton later = HoverButton.Glyph(Icons.ChevronUp, "A day later", side: 12, iconSize: 8);
    private DateTime date = DateTime.Today;

    public event Action? Changed;

    public PracticeDateStepper()
    {
        Focusable = true;
        text.VerticalAlignment = VerticalAlignment.Center;
        earlier.CornerRadius = later.CornerRadius = new CornerRadius(3);
        earlier.Click += () => Step(-1);
        later.Click += () => Step(1);
        foreach (var child in new Control[] { well, text, later, earlier }) Children.Add(child);
        AutomationProperties.SetName(this, "Interview date");
        Refresh();
    }

    /// The earliest day that can be chosen.
    public DateTime Minimum { get; set; } = DateTime.MinValue;

    public DateTime Date
    {
        get => date;
        set
        {
            date = value.Date < Minimum.Date ? Minimum.Date : value.Date;
            Refresh();
        }
    }

    public string DateText => text.Text ?? "";

    public void Step(int days)
    {
        if (!IsEffectivelyEnabled) return;
        var before = date;
        Date = date.AddDays(days);
        if (date != before) Changed?.Invoke();
    }

    private void Refresh() => text.Text = date.ToString("d", System.Globalization.CultureInfo.CurrentCulture);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var days = e.Key switch { Key.Up => 1, Key.Down => -1, Key.PageUp => 30, Key.PageDown => -30, _ => 0 };
        if (days == 0) return;
        Step(days);
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) Opacity = IsEffectivelyEnabled ? 1 : Token.State.DisabledAlpha;
    }

    protected override void LayoutFrames(Size size)
    {
        Place(well, 0, 0, size.Width - 16, size.Height);
        Place(text, 3, 0, size.Width - 22, size.Height);
        Place(later, size.Width - 13, 0, 12, size.Height / 2);
        Place(earlier, size.Width - 13, size.Height / 2, 12, size.Height / 2);
    }
}

/// How far the session is: up to ten segments, the ones behind filled and the current one rimmed.
public sealed class PracticeProgressSegmentsView : Control
{
    private int completed;
    private int total;
    private int currentIndex;

    public int Completed
    {
        get => completed;
        set { completed = value; InvalidateVisual(); }
    }

    public int Total
    {
        get => total;
        set { total = value; InvalidateVisual(); }
    }

    public int CurrentIndex
    {
        get => currentIndex;
        set { currentIndex = value; InvalidateVisual(); }
    }

    public int QueuedReviews { get; set; }

    public override void Render(DrawingContext context)
    {
        if (total <= 0) return;
        var count = Math.Min(total, 10);
        var done = Math.Min(count, (int)Math.Ceiling((double)Math.Max(0, completed) / total * count));
        var current = Math.Min(count - 1, Math.Max(0, (int)Math.Floor((double)Math.Max(0, currentIndex) / total * count)));
        const double gap = 5;
        var width = Math.Max(4, (Bounds.Width - (count - 1) * gap) / count);
        var height = Math.Min(6, Bounds.Height);
        var top = Math.Floor((Bounds.Height - height) / 2);
        for (var index = 0; index < count; index++)
        {
            var rect = new Rect(index * (width + gap), top, width, height);
            var color = index < done ? PracticeUi.Blue : index == current ? PracticeUi.Blue.WithAlpha(0.48) : Token.Palette.White.WithAlpha(0.24);
            var rim = index == current && index >= done ? new Pen(PracticeUi.Blue.Brush(), 1) : null;
            context.DrawRectangle(color.Brush(), rim, rect, height / 2, height / 2);
        }
    }
}
