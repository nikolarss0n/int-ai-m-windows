using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

public enum FormattingCommand
{
    Heading,
    Bold,
    Italic,
    Code,
    CodeBlock,
    List,
    Link,
    Divider
}

/// The Context tab: the notes, written in Markdown, with the formatting bar that floats over them
/// while they are edited and the search field that comes up on demand.
public sealed class NotesView : FrameView
{
    /// The Mac's notes area sits 20 in from the window and 56 above its foot; the text scrolls 15 inside that.
    private const double EditorInset = 35;
    private const double EditorBottom = 71;

    public NotesView()
    {
        Children.Add(Editor);
        Children.Add(Toolbar);
        Children.Add(Search);
    }

    public NotesEditor Editor { get; } = Ui.Named(new NotesEditor(), "Notes");

    public FormattingToolbarView Toolbar { get; } = new() { IsVisible = false, Opacity = 0 };

    public NotesSearchBar Search { get; } = new() { IsVisible = false, Opacity = 0 };

    protected override void LayoutFrames(Size size)
    {
        Place(Editor, EditorInset, EditorInset, size.Width - EditorInset * 2, size.Height - EditorInset - EditorBottom);
        if (Toolbar.IsVisible) Place(Toolbar, (size.Width - FormattingToolbarView.BarWidth) / 2, 36, FormattingToolbarView.BarWidth, FormattingToolbarView.BarHeight);
        if (Search.IsVisible) Place(Search, size.Width / 2 - NotesSearchBar.BarWidth / 2, 110, NotesSearchBar.BarWidth, NotesSearchBar.BarHeight);
    }
}

/// Eight buttons that put Markdown around the selection.
public sealed class FormattingToolbarView : FrameView
{
    public const double BarWidth = 360;
    public const double BarHeight = 44;
    public const double ShownOpacity = 1;

    private readonly Border surface = new()
    {
        Background = Token.Palette.Graphite.Brush(),
        BorderBrush = Token.Palette.White.WithAlpha(0.3).Brush(),
        BorderThickness = new Thickness(Token.Border.Emphasis),
        CornerRadius = new CornerRadius(12),
    };
    private readonly List<HoverButton> buttons = new();

    public FormattingToolbarView()
    {
        Children.Add(surface);
        Add(Icons.TextSize, "Heading", FormattingCommand.Heading);
        Add(Icons.Bold, "Bold", FormattingCommand.Bold);
        Add(Icons.Italic, "Italic", FormattingCommand.Italic);
        Add(Icons.Code, "Code", FormattingCommand.Code);
        Add(Icons.Braces, "Code Block", FormattingCommand.CodeBlock);
        Add(Icons.ListBullet, "Bullet List", FormattingCommand.List);
        Add(Icons.Link, "Link", FormattingCommand.Link);
        Add(Icons.Minus, "Divider", FormattingCommand.Divider);
    }

    public event Action<FormattingCommand>? Chosen;

    public IReadOnlyList<HoverButton> Buttons => buttons;

    private void Add(IconShape icon, string name, FormattingCommand command)
    {
        var button = new HoverButton { CornerRadius = new CornerRadius(6), Icon = icon, IconSize = 15, Label = name, Help = name, GlyphColor = Token.Palette.White };
        // A press must not take the keyboard from the notes: the selection is what the button acts on.
        button.Focusable = false;
        button.Click += () => Chosen?.Invoke(command);
        buttons.Add(button);
        Children.Add(button);
    }

    protected override Size PreferredSize(Size available) => new(BarWidth, BarHeight);

    protected override void LayoutFrames(Size size)
    {
        Place(surface, 0, 0, size.Width, size.Height);
        var x = 8.0;
        foreach (var button in buttons)
        {
            Place(button, x, 7, 40, 30);
            x += 44;
        }
    }
}

/// The search field for the notes, with the count of what it found.
public sealed class NotesSearchBar : FrameView
{
    public const double BarWidth = 400;
    public const double BarHeight = 50;

    private readonly Border surface = new()
    {
        Background = Token.Palette.Storm.Brush(),
        BorderBrush = Token.Palette.White.WithAlpha(0.25).Brush(),
        BorderThickness = new Thickness(Token.Border.Emphasis),
        CornerRadius = new CornerRadius(14),
    };
    private readonly TextBlock result = Ui.Label("", 11, Token.Font.Medium, Token.Palette.Green);

    public NotesSearchBar()
    {
        result.TextAlignment = TextAlignment.Right;
        // The theme's field goes black with a thick ring when it has the keyboard; this one stays a quiet well.
        var well = Token.Palette.White.WithAlpha(0.08).Brush();
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" }) Field.Resources[key] = well;
        foreach (var key in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver" }) Field.Resources[key] = Token.Colors.BorderHover.Brush();
        Field.Resources["TextControlBorderBrushFocused"] = Token.Colors.FocusRing.Brush();
        Field.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1.5);
        Field.Resources["TextControlForegroundFocused"] = Token.Colors.TextPrimary.Brush();
        Field.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Submitted?.Invoke(Field.Text ?? "");
        };
        Children.Add(surface);
        Children.Add(Field);
        Children.Add(result);
    }

    public event Action<string>? Submitted;

    public TextBox Field { get; } = Ui.Named(new TextBox
    {
        PlaceholderText = "Search notes... (e.g., hashmap)",
        FontSize = 13,
        FontFamily = Token.Font.Family,
        MinHeight = 0,
        Padding = new Thickness(7, 2),
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        CornerRadius = new CornerRadius(6),
    }, "Search notes");

    public string ResultText => result.Text ?? "";

    public void ShowCount(int? count)
    {
        result.Text = count switch
        {
            null => "",
            0 => "✗ 0",
            _ => $"✓ {count}",
        };
        result.Foreground = (count == 0 ? Token.Palette.Red : Token.Palette.Green).Brush();
    }

    public void Reset()
    {
        Field.Text = "";
        ShowCount(null);
    }

    protected override Size PreferredSize(Size available) => new(BarWidth, BarHeight);

    protected override void LayoutFrames(Size size)
    {
        Place(surface, 0, 0, size.Width, size.Height);
        Place(Field, 15, 11, 300, 26);
        Place(result, 320, 18, 70, 16);
    }
}

/// The templates to load into the notes: a row of categories, and under it each template with its questions.
public sealed class TemplatePickerView : FrameView
{
    public const double PanelWidth = 480;
    public const double PanelHeight = 520;

    private readonly Border backdrop = new() { Background = Token.Colors.BgSolidWindow.Brush() };
    private readonly Border track = new() { Background = Token.Colors.BgInset.Brush(), CornerRadius = new CornerRadius(Token.Radius.Sm) };
    private readonly List<HoverButton> segments = new();
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly CardList list;
    private TemplateCategory category = TemplateCategory.Behavioral;

    /// The panel is a solid window, so the chosen segment keeps its fill whatever the main window does.
    private static readonly Token.ControlStyle SelectedSegment = new(
        Token.Colors.IconSelected, Token.Colors.BgSelected, Token.Colors.BgSelected, Token.Colors.BgPressed, Token.Colors.Clear, Token.Colors.Clear, 0);

    public TemplatePickerView()
    {
        list = new CardList(this);
        scroll.Content = list;
        Children.Add(backdrop);
        Children.Add(track);
        foreach (var each in TemplateCategories.AllCases)
        {
            var chosen = each;
            var segment = new HoverButton { CornerRadius = new CornerRadius(Token.Radius.Sm - 1), Title = each.DisplayName(), TitleSize = 12, TitleWeight = Token.Font.Medium, Label = each.DisplayName() };
            segment.Click += () => Show(chosen);
            segments.Add(segment);
            Children.Add(segment);
        }
        Children.Add(scroll);
        Show(category);
    }

    /// The person chose a template to load.
    public event Action<InterviewTemplate>? Loaded;

    public TemplateCategory Category => category;

    public void Show(TemplateCategory next)
    {
        category = next;
        for (var index = 0; index < segments.Count; index++)
        {
            var selected = TemplateCategories.AllCases[index] == next;
            segments[index].Apply(selected ? SelectedSegment : Token.ControlStyle.Ghost);
            segments[index].TitleColor = selected ? Token.Colors.TextPrimary : Token.Colors.TextBody;
        }
        list.Show(BuiltInTemplates.TemplatesForCategory(next));
        scroll.Offset = default;
        Relayout();
    }

    protected override Size PreferredSize(Size available) =>
        new(available.Width > 0 ? available.Width : PanelWidth, available.Height > 0 ? available.Height : PanelHeight);

    protected override void LayoutFrames(Size size)
    {
        Place(backdrop, 0, 0, size.Width, size.Height);
        var trackWidth = size.Width - 40;
        Place(track, 20, 20, trackWidth, 30);
        var widths = segments.Select(segment => Ui.TextWidth(segment.Title, 12, Token.Font.Medium) + 20).ToList();
        var scale = (trackWidth - 4) / Math.Max(1, widths.Sum());
        var x = 22.0;
        for (var index = 0; index < segments.Count; index++)
        {
            var width = widths[index] * scale;
            Place(segments[index], x, 22, width, 26);
            x += width;
        }
        Place(scroll, 0, 60, size.Width, size.Height - 60);
    }

    /// The cards of one category, one under the other.
    private sealed class CardList : FrameView
    {
        private readonly TemplatePickerView owner;
        private readonly List<(Border Card, InterviewTemplate Template, List<Control> Parts)> cards = new();

        public CardList(TemplatePickerView owner) => this.owner = owner;

        public void Show(IReadOnlyList<InterviewTemplate> templates)
        {
            Children.Clear();
            cards.Clear();
            foreach (var template in templates)
            {
                var card = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = Token.Palette.White.WithAlpha(0.055).Brush(),
                    BorderBrush = Token.Palette.White.WithAlpha(0.10).Brush(),
                    BorderThickness = new Thickness(1),
                };
                var title = Ui.Label(template.Name, 14, Token.Font.Semibold, Token.Palette.White);
                var description = Ui.Label(template.Description, 11, color: Token.Palette.White.WithAlpha(0.5));
                var load = HoverButton.Chip(
                    "Load", Token.ControlStyle.FollowUp,
                    template.Category == TemplateCategory.TestAutomation ? Token.Palette.SystemCyan : Token.Palette.White, height: 28);
                load.TitleSize = 12;
                load.TitleWeight = Token.Font.Medium;
                load.Label = $"Load {template.Name}";
                load.Click += () => owner.Loaded?.Invoke(template);
                var parts = new List<Control> { title, description, load };
                foreach (var question in template.Questions)
                {
                    var color = question.Difficulty switch
                    {
                        TemplateDifficulty.Easy => Token.Palette.Green,
                        TemplateDifficulty.Medium => Token.Palette.Orange,
                        _ => Token.Palette.Red,
                    };
                    parts.Add(new Border { CornerRadius = new CornerRadius(3), Background = color.Brush() });
                    parts.Add(Ui.Label(question.Text, 11, color: Token.Palette.White.WithAlpha(0.7)));
                }
                Children.Add(card);
                foreach (var part in parts) Children.Add(part);
                cards.Add((card, template, parts));
            }
            Relayout();
        }

        private static double CardHeight(InterviewTemplate template) => 60 + template.Questions.Count * 28;

        protected override Size PreferredSize(Size available) =>
            new(available.Width, 10 + cards.Sum(card => CardHeight(card.Template) + 10) + 10);

        protected override void LayoutFrames(Size size)
        {
            var y = 10.0;
            foreach (var (card, template, parts) in cards)
            {
                var width = size.Width - 30;
                var height = CardHeight(template);
                Place(card, 15, y, width, height);
                Place(parts[0], 15 + 15, y + 8, width - 120, 20);
                Place(parts[1], 15 + 15, y + 32, width - 120, 16);
                Place(parts[2], 15 + width - 95, y + 7, 80, 28);
                var row = y + 44;
                for (var index = 3; index < parts.Count; index += 2)
                {
                    Place(parts[index], 15 + 20, row + 6, 6, 6);
                    Place(parts[index + 1], 15 + 32, row, width - 50, 16);
                    row += 28;
                }
                y += height + 10;
            }
        }
    }
}
