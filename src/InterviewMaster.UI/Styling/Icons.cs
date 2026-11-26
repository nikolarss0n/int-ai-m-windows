using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace InterviewMaster.Presentation;

/// A glyph drawn on a 16 by 16 grid. `Filled` shapes are filled (holes use the even-odd rule);
/// the rest are stroked with round ends.
public sealed record IconShape(string Data, bool Filled = false)
{
    private Geometry? geometry;

    public Geometry Geometry => geometry ??= Geometry.Parse(Data);
}

/// The app's glyphs, drawn as vectors so they look the same on every Windows version and can be
/// checked on any machine. Each stands in for the SF Symbol the Mac app uses in the same place.
public static class Icons
{
    public static readonly IconShape Document = new("M4 1.75h5l3 3v9.5H4z M9 1.75v3h3 M6 8.5h4 M6 11h4");
    public static readonly IconShape Clock = new("M8 1.75a6.25 6.25 0 1 0 0 12.5a6.25 6.25 0 1 0 0-12.5z M8 4.6V8H5.4");
    public static readonly IconShape GraduationCap = new("M1.5 6 8 3l6.5 3L8 9z M4 7.3v3.1c1 1 2.4 1.6 4 1.6s3-.6 4-1.6V7.3 M14.5 6v4");
    public static readonly IconShape Code = new("M5.4 4.5 2 8l3.4 3.5 M10.6 4.5 14 8l-3.4 3.5 M9.2 3 6.8 13");
    public static readonly IconShape Play = new("M3.4 1.9v12.2L14 8z", Filled: true);
    public static readonly IconShape Stop = new("M3.8 2.3h8.4a1.5 1.5 0 0 1 1.5 1.5v8.4a1.5 1.5 0 0 1-1.5 1.5H3.8a1.5 1.5 0 0 1-1.5-1.5V3.8a1.5 1.5 0 0 1 1.5-1.5z", Filled: true);
    public static readonly IconShape Speaker = new("M2 6.25h2.4L8 3.4v9.2L4.4 9.75H2z M10.4 5.9a3 3 0 0 1 0 4.2 M12.3 4.1a5.5 5.5 0 0 1 0 7.8");
    public static readonly IconShape Microphone = new("M8 1.75a2 2 0 0 0-2 2V8a2 2 0 0 0 4 0V3.75a2 2 0 0 0-2-2z M3.75 7.5V8a4.25 4.25 0 0 0 8.5 0v-.5 M8 12.25v2 M5.75 14.25h4.5");
    public static readonly IconShape MicrophoneSlash = new("M8 1.75a2 2 0 0 0-2 2V8a2 2 0 0 0 4 0V3.75a2 2 0 0 0-2-2z M3.75 7.5V8a4.25 4.25 0 0 0 8.5 0v-.5 M8 12.25v2 M5.75 14.25h4.5 M2.5 2.5l11 11");
    public static readonly IconShape Ear = new("M3.9 11.2V6.3a4.2 4.2 0 1 1 8.3 1c-.4 1.5-1.6 1.9-2 3.1-.4 1.3-.5 3.7-2.6 3.7-1.5 0-2.5-1.1-2.5-2.5 M6.3 9.7V6.5a1.9 1.9 0 1 1 3.6.8c-.4.8-1.1 1-1.3 1.7-.2.7-.7 1-1.2 1");
    public static readonly IconShape Waveform = new("M2.5 6.5v3 M5.25 4.5v7 M8 2.5v11 M10.75 5v6 M13.5 6.5v3");
    public static readonly IconShape WaveformSlash = new("M2.5 6.5v3 M5.25 4.5v7 M8 2.5v11 M10.75 5v6 M13.5 6.5v3 M2.2 13.8 13.8 2.2");
    public static readonly IconShape Switching = new("M3 6.5a5.2 5.2 0 0 1 9.4-1.6 M13 9.5a5.2 5.2 0 0 1-9.4 1.6 M12.6 2.4v2.7H9.9 M3.4 13.6v-2.7h2.7");
    public static readonly IconShape Export = new("M8 10V1.9 M5.2 4.5 8 1.9l2.8 2.6 M5 7H3.75v7.25h8.5V7H11");
    public static readonly IconShape ChevronDown = new("M3.5 6 8 10.5 12.5 6");
    public static readonly IconShape ChevronUp = new("M3.5 10 8 5.5l4.5 4.5");
    public static readonly IconShape ChevronLeft = new("M10 3.5 5.5 8l4.5 4.5");
    public static readonly IconShape ChevronRight = new("M6 3.5 10.5 8 6 12.5");
    public static readonly IconShape Close = new("M4 4l8 8 M12 4l-8 8");
    public static readonly IconShape Copy = new("M5.75 4.75h6.5v9.5h-6.5z M3.75 11.4V1.75h6.4");
    public static readonly IconShape Checkmark = new("M3 8.5 6.5 12 13 4.5");
    public static readonly IconShape Trash = new("M3 4.25h10 M6.25 4.25V2.5h3.5v1.75 M4.25 4.25l.6 10h6.3l.6-10 M6.75 6.75v5 M9.25 6.75v5");
    public static readonly IconShape Camera = new("M2 5h2.6l1.2-1.75h4.4L11.4 5H14v8H2z M8 6.6a2.4 2.4 0 1 0 0 4.8a2.4 2.4 0 1 0 0-4.8z");
    public static readonly IconShape Search = new("M7 2.25a4.75 4.75 0 1 0 0 9.5a4.75 4.75 0 1 0 0-9.5z M10.5 10.5l3.25 3.25");
    public static readonly IconShape ListBullet = new("M2.5 4h.1 M2.5 8h.1 M2.5 12h.1 M5.5 4h8 M5.5 8h8 M5.5 12h8");
    public static readonly IconShape Braces = new("M6 2.5c-1.6 0-2 .7-2 2v1.7c0 1-.5 1.6-1.5 1.8 1 .2 1.5.8 1.5 1.8v1.7c0 1.3.4 2 2 2 M10 2.5c1.6 0 2 .7 2 2v1.7c0 1 .5 1.6 1.5 1.8-1 .2-1.5.8-1.5 1.8v1.7c0 1.3-.4 2-2 2");
    public static readonly IconShape Sparkles = new("M6.5 2.5l1.1 3.4L11 7l-3.4 1.1-1.1 3.4-1.1-3.4L2 7l3.4-1.1z M12 9.5l.6 1.9 1.9.6-1.9.6-.6 1.9-.6-1.9-1.9-.6 1.9-.6z");
    public static readonly IconShape Bolt = new("M9.2 1.5 3.5 9h3.7l-.9 5.5L12 7H8.3z", Filled: true);
    public static readonly IconShape Person = new("M8 2a2.75 2.75 0 1 0 0 5.5A2.75 2.75 0 1 0 8 2z M2.75 14c.4-2.9 2.5-4.5 5.25-4.5s4.85 1.6 5.25 4.5");
    public static readonly IconShape TextBubble = new("M2 3h12v8H7l-3 2.75V11H2z M4.75 5.75h6.5 M4.75 8.25h4");
    public static readonly IconShape Plus = new("M8 3v10 M3 8h10");
    public static readonly IconShape Minus = new("M3 8h10");
    public static readonly IconShape Link = new("M6.8 9.2a2.6 2.6 0 0 0 3.7 0l2.2-2.2a2.6 2.6 0 0 0-3.7-3.7l-.8.8 M9.2 6.8a2.6 2.6 0 0 0-3.7 0L3.3 9a2.6 2.6 0 0 0 3.7 3.7l.8-.8");
    public static readonly IconShape Info = new("M8 1.5a6.5 6.5 0 1 0 0 13a6.5 6.5 0 1 0 0-13z M7.25 7h1.5v5h-1.5z M7.25 4h1.5v1.5h-1.5z", Filled: true);
    public static readonly IconShape Success = new("M8 1.5a6.5 6.5 0 1 0 0 13a6.5 6.5 0 1 0 0-13z M4.5 8.2 5.6 7.1 7.2 8.7 10.4 5.2 11.5 6.3 7.2 10.9z", Filled: true);
    public static readonly IconShape Warning = new("M8 1.6 15 14H1z M7.25 6h1.5v4h-1.5z M7.25 11h1.5v1.5h-1.5z", Filled: true);
    public static readonly IconShape Error = new("M5.3 1.5h5.4l3.8 3.8v5.4l-3.8 3.8H5.3l-3.8-3.8V5.3z M7.25 4h1.5v5h-1.5z M7.25 10.5h1.5v1.5h-1.5z", Filled: true);
    public static readonly IconShape TextSize = new("M1.5 12.5 4 6l2.5 6.5 M2.4 10.4h3.2 M7.5 12.5 11 3.5l3.5 9 M8.8 9.4h4.4");
    public static readonly IconShape Bold = new("M4.75 2.75h3.9a2.45 2.45 0 0 1 0 4.9h-3.9z M4.75 7.65h4.5a2.8 2.8 0 0 1 0 5.6h-4.5z");
    public static readonly IconShape Italic = new("M6.75 2.75h4.75 M4.5 13.25h4.75 M9.6 2.75 6.4 13.25");
    public static readonly IconShape Lightbulb = new("M8 1.2a4.6 4.6 0 0 0-2.7 8.3c.5.4.8 1 .8 1.6v.5h3.8v-.5c0-.6.3-1.2.8-1.6A4.6 4.6 0 0 0 8 1.2z M6.2 12.5h3.6v1H6.2z M6.9 14.3h2.2v.7H6.9z", Filled: true);
    public static readonly IconShape CheckCircle = new("M8 1.5a6.5 6.5 0 1 0 0 13a6.5 6.5 0 1 0 0-13z M5.1 8.3l2 2L10.9 6");
    public static readonly IconShape Retry = new("M13 8a5 5 0 1 1-1.6-3.7 M13.2 2.4v3.2H10");
    public static readonly IconShape Trend = new("M2 2.5v11h12 M4.5 10.5 7.5 7.5l2 2 4-4.5 M10.8 5.5h2.7v2.7");
    public static readonly IconShape Calendar = new("M2.5 3.75h11v9.75h-11z M2.5 6.75h11 M5.25 2v3 M10.75 2v3");
    public static readonly IconShape Pencil = new("M3 13l.6-2.9 7.3-7.3 2.3 2.3-7.3 7.3z M9.6 4.1l2.3 2.3");
    public static readonly IconShape QuestionCircle = new("M8 1.5a6.5 6.5 0 1 0 0 13a6.5 6.5 0 1 0 0-13z M6.2 6.3a1.85 1.85 0 1 1 2.7 1.6c-.6.35-.9.7-.9 1.4 M8 11.2v.1");
    public static readonly IconShape PlayCircle = new("M8 1.5a6.5 6.5 0 1 0 0 13a6.5 6.5 0 1 0 0-13z M6.6 5.3v5.4L11 8z", Filled: true);
    public static readonly IconShape Swap = new("M2 5.5h11 M10.5 3 13 5.5l-2.5 2.5 M14 10.5H3 M5.5 8 3 10.5 5.5 13");
    public static readonly IconShape Undo = new("M5.5 3.5 2.5 6.5l3 3 M2.5 6.5h7a3.5 3.5 0 0 1 0 7H6");
    public static readonly IconShape Circle = new("M8 1.5a6.5 6.5 0 1 0 0 13a6.5 6.5 0 1 0 0-13z");
    public static readonly IconShape PersonFilled = new("M8 2a2.75 2.75 0 1 0 0 5.5A2.75 2.75 0 1 0 8 2z M2.75 14a5.25 5.25 0 0 1 10.5 0z", Filled: true);
    public static readonly IconShape Gear = new(GearData());

    /// The glyph for a macOS symbol name kept in the shared core.
    public static IconShape ForSymbol(string symbol) => symbol switch
    {
        "waveform.slash" => WaveformSlash,
        "arrow.triangle.2.circlepath" => Switching,
        "ear" => Ear,
        "waveform" => Waveform,
        "mic.slash.fill" or "mic.slash" => MicrophoneSlash,
        "mic" => Microphone,
        "speaker.wave.2" => Speaker,
        _ => Info
    };

    /// A cog with eight teeth around a hub.
    private static string GearData()
    {
        const int teeth = 8;
        const double outer = 6.4, inner = 4.9, centre = 8;
        var parts = new List<string>();
        for (var index = 0; index < teeth; index++)
        {
            var step = Math.PI * 2 / teeth;
            var angle = index * step;
            // Each tooth: up the flank, across the top, down the other flank, along the root.
            var points = new[]
            {
                (inner, angle - step * 0.30), (outer, angle - step * 0.17), (outer, angle + step * 0.17), (inner, angle + step * 0.30)
            };
            foreach (var (radius, at) in points)
            {
                var x = centre + radius * Math.Sin(at);
                var y = centre - radius * Math.Cos(at);
                parts.Add(FormattableString.Invariant($"{(parts.Count == 0 ? "M" : "L")}{x:0.###} {y:0.###}"));
            }
        }
        return string.Join(" ", parts) + "z M8 5.9a2.1 2.1 0 1 0 0 4.2a2.1 2.1 0 1 0 0-4.2z";
    }
}

/// Draws one glyph at a size, in a colour.
public sealed class IconView : Control
{
    public static readonly StyledProperty<IconShape?> ShapeProperty = AvaloniaProperty.Register<IconView, IconShape?>(nameof(Shape));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<IconView, IBrush?>(nameof(Foreground));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<IconView, double>(nameof(Size), Token.Sizing.Icon);

    static IconView()
    {
        AffectsRender<IconView>(ShapeProperty, ForegroundProperty, SizeProperty);
        AffectsMeasure<IconView>(SizeProperty);
    }

    public IconView()
    {
        IsHitTestVisible = false;
    }

    public IconView(IconShape shape, Color color, double size = Token.Sizing.Icon) : this()
    {
        Shape = shape;
        Foreground = color.Brush();
        Size = size;
    }

    public IconShape? Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Shape is not { } shape || Foreground is not { } brush) return;
        var scale = Size / 16;
        using var transform = context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - Size) / 2, (Bounds.Height - Size) / 2));
        if (shape.Filled) context.DrawGeometry(brush, null, shape.Geometry);
        // The line keeps its weight at every size, as a system glyph does.
        else context.DrawGeometry(null, new Pen(brush, 1.45 / Math.Min(1, scale), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), shape.Geometry);
    }
}
