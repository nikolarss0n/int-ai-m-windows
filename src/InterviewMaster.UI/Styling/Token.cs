using Avalonia;
using Avalonia.Media;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// Design tokens for the interview window: the answer card, question list, command bar and the code
/// panel. A port of `Presentation/Styling/DesignTokens.swift`, with the same three tiers:
/// - Global: raw values (palette, opacity ramp, scales). Only this file reads them.
/// - Alias: what a value means (text, background, border, hue). Views use these.
/// - Component: one component's decisions, composed from aliases (surface and control styles).
///
/// Views hold no raw colour, alpha, radius or font size. The palette is light on dark.
public static class Token
{
    private static Color Rgb(double red, double green, double blue, double alpha = 1) =>
        Color.FromArgb(Channel(alpha), Channel(red), Channel(green), Channel(blue));

    private static Color Gray(double white, double alpha = 1) => Rgb(white, white, white, alpha);

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    public static Color WithAlpha(this Color color, double alpha) => Color.FromArgb(Channel(alpha), color.R, color.G, color.B);

    /// This colour with its own alpha scaled.
    public static Color Faded(this Color color, double factor) => Color.FromArgb((byte)Math.Round(color.A * factor), color.R, color.G, color.B);

    public static Color Tinted(this Color color, Tint level) => color.WithAlpha(level.Alpha());

    public static Color Edged(this Color color, Edge level) => color.WithAlpha(level.Alpha());

    public static IBrush Brush(this Color color) => new ImmutableSolidColorBrushCache(color).Brush;

    // MARK: - Tier 1: Global

    public static class Palette
    {
        public static readonly Color White = Gray(1);
        public static readonly Color Black = Gray(0);
        public static readonly Color Green = Rgb(0.204, 0.780, 0.349);
        public static readonly Color GreenLight = Rgb(0.412, 0.905, 0.512);
        public static readonly Color Gold = Rgb(1.0, 0.84, 0.0);
        public static readonly Color Red = Rgb(1.0, 0.231, 0.188);
        public static readonly Color RedLight = Rgb(1.0, 0.88, 0.86);
        /// The app's accent.
        public static readonly Color Purple = Rgb(0.686, 0.322, 0.871);
        public static readonly Color SystemPurple = Rgb(0.749, 0.353, 0.949);
        public static readonly Color Magenta = Rgb(0.80, 0.27, 0.93);
        public static readonly Color MagentaLight = Rgb(0.93, 0.68, 1.0);
        /// Code.
        public static readonly Color Cyan = Rgb(0.35, 0.78, 0.98);
        public static readonly Color CyanLight = Rgb(0.62, 0.89, 1.0);
        public static readonly Color Charcoal = Gray(0.035);
        public static readonly Color Slate = Rgb(0.075, 0.085, 0.10);
        /// The near-black of the surfaces that are read.
        public static readonly Color Graphite = Rgb(0.105, 0.105, 0.115);
        /// The cool gray of the surfaces that are glanced at.
        public static readonly Color Storm = Rgb(0.15, 0.16, 0.185);
        public static readonly Color Orange = Rgb(1.0, 0.624, 0.039);
        public static readonly Color Pink = Rgb(1.0, 0.216, 0.373);
        public static readonly Color SystemCyan = Rgb(0.392, 0.824, 1.0);
        public static readonly Color Blue = Rgb(0.039, 0.518, 1.0);
        public static readonly Color Yellow = Rgb(1.0, 0.839, 0.039);
        public static readonly Color SystemGreen = Rgb(0.188, 0.820, 0.345);
        public static readonly Color SystemRed = Rgb(1.0, 0.271, 0.227);
        public static readonly Color Gray50 = Gray(0.5);
    }

    /// One opacity ramp shared by text, lines, washes and scrims.
    public static class Opacity
    {
        public const double O06 = 0.06;
        public const double O10 = 0.10;
        public const double O16 = 0.16;
        public const double O28 = 0.28;
        public const double O30 = 0.30;
        public const double O45 = 0.45;
        public const double O55 = 0.55;
        public const double O66 = 0.66;
        public const double O72 = 0.72;
        public const double O80 = 0.80;
        public const double O85 = 0.85;
        public const double O90 = 0.90;
        public const double O94 = 0.94;
    }

    // MARK: - Tier 2: Alias

    /// How strongly a hue washes the surface behind its content.
    public enum Tint { Faint, Soft, Medium, Strong }

    public static double Alpha(this Tint tint) => tint switch
    {
        Tint.Faint => Opacity.O06,
        Tint.Soft => Opacity.O10,
        Tint.Medium => Opacity.O16,
        _ => Opacity.O28
    };

    /// How strongly a hue outlines a surface.
    public enum Edge { Faint, Soft, Regular, Strong, Active }

    public static double Alpha(this Edge edge) => edge switch
    {
        Edge.Faint => Opacity.O16,
        Edge.Soft => Opacity.O30,
        Edge.Regular => Opacity.O45,
        Edge.Strong => Opacity.O55,
        _ => Opacity.O80
    };

    public static class Colors
    {
        // Text, strongest to weakest. There is no step below `TextMuted`.
        public static readonly Color TextPrimary = Palette.White;
        public static readonly Color TextStrong = Palette.White.WithAlpha(Opacity.O94);
        /// Reading text.
        public static readonly Color TextBody = Palette.White.WithAlpha(Opacity.O85);
        /// Labels and captions.
        public static readonly Color TextMuted = Palette.White.WithAlpha(Opacity.O72);

        public static readonly Color IconDefault = TextBody;
        public static readonly Color IconSelected = TextPrimary;

        /// The filled surfaces of the Solid level.
        public static readonly Color BgCard = Palette.Graphite;
        public static readonly Color BgWidget = Palette.Storm;
        public static readonly Color BgCodePanel = Palette.Graphite;
        public static readonly Color BgCode = Palette.Slate;
        public static readonly Color BgChrome = Palette.Graphite;
        /// The dark layer behind the content, which the background slider strengthens.
        public static Color BgGlassFloor(double alpha) => Palette.Graphite.WithAlpha(alpha);
        /// Behind everything when the system asks for less transparency.
        public static readonly Color BgSolidWindow = Palette.Graphite;
        /// The two ends of the fade that says a card has more below.
        public static readonly Color BgFadeClear = Palette.Graphite.WithAlpha(0);
        public static Color BgFadeSolid => Transparency.Floating ? BgFadeClear : BgCard;
        /// A card resting on a card.
        public static readonly Color BgInset = Palette.White.Tinted(Tint.Faint);
        public static readonly Color BgHover = Palette.White.Tinted(Tint.Soft);
        public static readonly Color BgPressed = Palette.White.Tinted(Tint.Medium);
        public static readonly Color BgSelected = Palette.White.Tinted(Tint.Medium);
        /// A bar standing where a line of text will be, and the highlight that passes over it.
        public static readonly Color BgSkeleton = Palette.White.Tinted(Tint.Soft);
        public static readonly Color BgSkeletonSweep = Palette.White.Tinted(Tint.Medium);
        public static readonly Color BgSkeletonSweepClear = Palette.White.WithAlpha(0);

        public static readonly Color BorderSubtle = Palette.White.WithAlpha(Opacity.O10);
        /// The light catching a widget's edge: seen, never read as a line.
        public static readonly Color BorderWidget = Palette.White.WithAlpha(Opacity.O06);
        public static readonly Color BorderDefault = Palette.White.Edged(Edge.Faint);
        public static readonly Color BorderHover = Palette.White.Edged(Edge.Soft);
        public static readonly Color Separator = Palette.White.Edged(Edge.Faint);
        /// The ring around the control that has the keyboard.
        public static readonly Color FocusRing = Rgb(0.35, 0.62, 1.0);
        public static readonly Color Clear = Palette.White.WithAlpha(0);
    }

    /// A hue and the two ways it is used. `Solid` is for fills, dots, washes and outlines. `Text` is
    /// for words, glyphs and rings drawn on those washes: a solid hue is too dark to read as text there.
    public sealed record Hue(Color Solid, Color Text)
    {
        public Color Tint(Tint level) => Solid.Tinted(level);
        public Color Edge(Edge level) => Solid.Edged(level);

        /// The app's own hue: selection, the answer the selection points at, and the app's name.
        public static readonly Hue Accent = new(Palette.Magenta, Palette.MagentaLight);
        /// Syntax and code marks only.
        public static readonly Hue Code = new(Palette.Cyan, Palette.CyanLight);
        /// Ready, listening, go.
        public static readonly Hue Success = new(Palette.Green, Palette.GreenLight);
        /// Working, or needs attention. Gold is already light enough to be its own text.
        public static readonly Hue Warning = new(Palette.Gold, Palette.Gold);
        /// Failed, stop.
        public static readonly Hue Danger = new(Palette.Red, Palette.RedLight);
        public static readonly Hue Neutral = new(Palette.White, Colors.TextBody);
    }

    public static class Space
    {
        public const double Xs = LayoutConstants.Spacing.Xs;
        public const double Sm = LayoutConstants.Spacing.Sm;
        public const double Md = LayoutConstants.Spacing.Md;
        /// Window and column inset.
        public const double Inset = 16;
    }

    public static class Sizing
    {
        public const double Control = LayoutConstants.IconButton.Size;
        public const double ControlSmall = 26;
        public const double Badge = 24;
        public const double BadgeSmall = 20;
        public const double Dot = 8;
        /// Height of a status pill.
        public const double Pill = 16;
        /// Height of the notice in the header: a little under the header's 34.
        public const double Notice = 28;
        public const double Icon = 16;
    }

    /// The bars of a loading placeholder: as tall as the line of text they stand in for.
    public static class Skeleton
    {
        public const double LineHeight = 12;
        public const double SmallLineHeight = 9;
        public const double Dot = 6;
    }

    public static class Radius
    {
        public const double Sm = LayoutConstants.CornerRadius.Small;
        public const double Md = LayoutConstants.CornerRadius.Medium;
        public const double Lg = LayoutConstants.CornerRadius.Large;
        /// A widget's corner.
        public const double Widget = 22;
        /// Concentric with a widget for something `inset` points inside it.
        public static double Inside(double inset) => Widget - inset;
        /// Full rounding for a shape this tall, so its ends are semicircles.
        public static double Full(double side) => side / 2;
    }

    public static class Border
    {
        public const double Hairline = 1;
        public const double Emphasis = 1.5;
    }

    /// Sentence-case text starts at `Caption`; `Micro` is only for capitals. Weights are regular,
    /// medium and semibold: emphasis is one step up, never bold.
    public static class Font
    {
        public const double Micro = LayoutConstants.Typography.CaptionSmall;
        public const double Caption = 12;
        public const double Body = LayoutConstants.Typography.Caption;
        public const double Lead = LayoutConstants.Typography.Body;
        public const double Title = LayoutConstants.Typography.BodyLarge;
        public const double Code = 14;

        /// Segoe on Windows. The names after it are for the Mac and Linux, where the UI driver runs.
        public static FontFamily Family { get; set; } = new("Segoe UI Variable Text, Segoe UI, Inter, Helvetica Neue, Arial");

        public static FontFamily MonoFamily { get; set; } = new("Cascadia Mono, Cascadia Code, Consolas, Menlo, DejaVu Sans Mono, Courier New");

        public static readonly FontWeight Regular = FontWeight.Normal;
        public static readonly FontWeight Medium = FontWeight.Medium;
        public static readonly FontWeight Semibold = FontWeight.SemiBold;

        /// Digits that keep their width as a value changes.
        public static readonly FontFeatureCollection TabularDigits = new() { FontFeature.Parse("tnum") };
    }

    /// Line height as a multiple of the font size.
    public static class Leading
    {
        /// Text read in lines: the live answer.
        public const double Reading = 1.5;
        /// Wrapped labels, descriptions, quotes and code.
        public const double Compact = 1.4;
        /// Headings: large type sets tight.
        public const double Heading = 1.25;
    }

    /// The longest line of text, about 74 characters at the size it is set in.
    public static class Measure
    {
        /// The live answer, in `Font.Lead`.
        public const double Reading = 470;
        /// The question quoted above it, and the CV strip, in `Font.Body`.
        public const double Caption = 440;
        /// A step's note in the code, in `Font.Caption`.
        public const double Note = 376;
    }

    public static class Text
    {
        /// Between the parts of a meta line. Two spaces would collapse or double; an en space does not.
        public const string Separator = " · ";
        /// Capitals get a twentieth of an em of letter-spacing so they do not crowd.
        public const double CapsTracking = 0.05;

        /// The room a label keeps on each side of its words, as an AppKit label does. Frames and
        /// widths are the macOS ones, so the words start two points inside them here too.
        public const double LabelInset = 2;
    }

    public sealed record Shadow(Color Color, double Opacity, double Radius, double OffsetX, double OffsetY);

    public static class Elevation
    {
        /// A widget resting on the desktop: a wide, soft shadow and no outline.
        public static readonly Shadow Raised = new(Palette.Black, 0.16, 12, 0, 3);
        public static readonly Shadow Floating = new(Palette.Black, 0.32, 20, 0, 6);
        /// Follows the shape of the text itself, because the view behind it has no fill.
        public static readonly Shadow TextHalo = new(Palette.Black, 0.75, 3, 0, 0);
    }

    /// The selected question: a wash in the accent with a bar down its leading edge.
    public static class Selection
    {
        /// How far the wash sits inside its row.
        public const double Inset = 6;
        public const double BarWidth = 3;
        /// From the wash's leading edge to the bar.
        public const double BarInset = 3;
        /// From the wash's top and bottom to the bar's ends.
        public const double BarVerticalInset = 7;
        public static readonly Color Wash = Hue.Accent.Tint(Tint.Medium);
        public static readonly Color Bar = Hue.Accent.Text;
        public static readonly double Radius = Token.Radius.Inside(Inset);
    }

    public static class Motion
    {
        /// How long the "Copied" checkmark stays before the copy icon returns.
        public const double FeedbackHold = 1.2;
        /// How long "Answer ready" stays in the activity capsule.
        public const double ReadyHold = 1.4;
        /// A wait shorter than this gets no indicator: it would flash and be gone.
        public const double LoadingDelay = 0.2;
        /// Content that arrives fades in over this long, and the placeholder it replaces fades out.
        public const double FadeIn = 0.18;
        /// One pass of the highlight across a placeholder.
        public const double Shimmer = 1.6;
        /// How often a wait counts its seconds.
        public const double ClockTick = 1;
        /// Whether the person asked the system for less motion. The app sets it from Windows; the UI
        /// driver replaces it to check both.
        public static Func<bool> ReducesMotion { get; set; } = () => false;
    }

    public static class State
    {
        /// A control with nothing to act on right now.
        public const double DisabledAlpha = Opacity.O55;
        /// Code that is not the step being typed: out of the way, still readable at a glance.
        public const double SetBackAlpha = Opacity.O72;
        /// A control that is available but has nothing to show yet.
        public const double IdleAlpha = Opacity.O55;
    }

    // MARK: - Tier 3: Component

    /// How a card, strip, pill or bar looks: shape, fill, outline and lift.
    public sealed record SurfaceStyle(double Radius, Color? Background = null, Color? Border = null, double BorderWidth = Token.Border.Hairline)
    {
        /// Text with nothing behind it.
        public static readonly SurfaceStyle FloatingText = new(0);

        private static SurfaceStyle Filled(double radius, Color background, bool bordered = true) =>
            Transparency.Floating ? FloatingText : new SurfaceStyle(radius, background, bordered ? Colors.BorderWidget : null);

        public static SurfaceStyle Card => Filled(Token.Radius.Widget, Colors.BgCard);
        public static SurfaceStyle CodePanel => Filled(Token.Radius.Widget, Colors.BgCodePanel);
        public static SurfaceStyle Briefing => Filled(Token.Radius.Widget, Colors.BgWidget);
        public static SurfaceStyle Strip => Filled(Token.Radius.Widget, Colors.BgWidget);
        public static SurfaceStyle CommandBar => Filled(Token.Radius.Full(Token.CommandBar.Height), Colors.BgChrome, bordered: false);
        public static readonly SurfaceStyle StepCard = new(Token.Radius.Inside(Space.Inset), Colors.BgInset);
        public static SurfaceStyle CodeWell => new(Token.Radius.Sm, Transparency.Floating ? null : Colors.BgCode);
        public static SurfaceStyle Banner => new(Token.Radius.Md, Transparency.Floating ? null : Hue.Warning.Tint(Tint.Soft));

        public static SurfaceStyle Badge(Hue hue) => new(Token.Radius.Full(Sizing.Badge), hue.Tint(Tint.Medium));
        public static SurfaceStyle Tag(Hue hue) => new(Token.Radius.Sm, hue.Tint(Tint.Medium));
        public static SurfaceStyle Pill(Hue hue, double height) => new(Token.Radius.Full(height), Transparency.Floating ? null : hue.Tint(Tint.Soft));
        /// A line of feedback in the header: washed in the hue of what it says.
        public static SurfaceStyle Notice(Hue hue) => new(Token.Radius.Full(Sizing.Notice), Transparency.Floating ? null : hue.Tint(Tint.Soft));
        public static SurfaceStyle Dot(Hue hue) => new(Token.Radius.Full(Sizing.Dot), hue.Solid);
        /// The wash behind the selected question.
        public static SurfaceStyle Highlight => new(Selection.Radius, Transparency.Floating ? null : Selection.Wash);
        /// The bar down the selected question's leading edge.
        public static readonly SurfaceStyle HighlightBar = new(Token.Radius.Full(Selection.BarWidth), Selection.Bar);
    }

    /// How a hover button looks at rest, hovered, pressed and outlined.
    public sealed record ControlStyle(Color? Glyph, Color Background, Color Hover, Color Press, Color Border, Color HoverBorder, double BorderWidth)
    {
        public static readonly ControlStyle Ghost = new(
            Colors.IconDefault, Colors.Clear, Colors.BgHover, Colors.BgPressed, Colors.Clear, Colors.BorderHover, 0);

        /// Controls have no resting fill while content floats; hover still answers the pointer.
        private static Color Rest(Color fill) => Transparency.Floating ? Colors.Clear : fill;

        public static ControlStyle GhostSelected => new(
            Colors.IconSelected, Rest(Colors.BgSelected), Colors.BgHover, Colors.BgPressed, Colors.Clear, Colors.Clear, 0);

        /// A ghost button whose panel is open: lit in the panel's hue.
        public static ControlStyle Selected(Hue hue) => new(
            hue.Text, Rest(hue.Tint(Tint.Medium)), hue.Tint(Tint.Strong), hue.Tint(Tint.Strong), Colors.Clear, Colors.Clear, 0);

        /// The main action of a bar, tinted in the hue of what it does.
        public static ControlStyle Primary(Hue hue) => new(
            hue.Text, Rest(hue.Tint(Tint.Soft)), hue.Tint(Tint.Medium), hue.Tint(Tint.Strong), Colors.Clear, Colors.Clear, 0);

        /// A button inside a notice, which is already washed in its hue.
        public static ControlStyle NoticeAction(Hue hue) => new(
            null, Rest(hue.Tint(Tint.Faint)), hue.Tint(Tint.Medium), hue.Tint(Tint.Strong), Colors.Clear, Colors.Clear, 0);

        /// A follow-up action: it keeps its fill over a floating window too, because it has to read as
        /// a button the moment it shows.
        public static readonly ControlStyle FollowUp = new(
            null, Hue.Neutral.Tint(Tint.Soft), Hue.Neutral.Tint(Tint.Medium), Hue.Neutral.Tint(Tint.Strong), Colors.Clear, Colors.Clear, 0);

        /// A small text action; neutral unless given a hue.
        public static ControlStyle Chip(Hue? hue = null)
        {
            var actual = hue ?? Hue.Neutral;
            return new(null, Rest(actual.Tint(Tint.Soft)), actual.Tint(Tint.Medium), actual.Tint(Tint.Strong), Colors.Clear, Colors.Clear, 0);
        }
    }

    public static class Header
    {
        public const double Height = 34;
        /// From the top of the window to the middle of the header row.
        public const double CenterY = 16;
        /// The most the status capsule grows to.
        public const double CapsuleWidth = 300;
        /// Where the title starts from the app icon's left edge.
        public const double TitleX = 30;
        public const double IconSize = 22;
        public const double MinimumTitleWidth = 260;
    }

    public static class CommandBar
    {
        public const double Height = 44;
        public const double BottomMargin = 14;
        public const double SideMargin = 28;
        public const double ItemGap = 6;
        public static readonly Thickness Padding = new(8, 6, 8, 6);
        /// Between the groups of the bar (navigation, start, audio); inside a group the gap is `ItemGap`.
        public const double GroupGap = 16;
        public const double CapsuleWidth = 162;
        /// The start and end button is the one control with a word on it.
        public const double PrimaryWidth = 84;
    }

    /// Vertical geometry of the interview window, top to bottom: the title row, the content, the
    /// question list (16 above the bar) and the bar. The list takes what its rows need and the card
    /// takes everything else, because the card is what is read.
    public static class FocusLayout
    {
        /// From the window's edge to the content's.
        public const double WindowInset = 12;
        /// Space from the top of the content to the first card, clear of the title row.
        public const double ContentTop = 26;
        /// The line above the code, and the space under it.
        public const double TalkStripHeight = 30;
        public const double TalkStripGap = 6;
        /// The strip when its questions sit side by side.
        public const double BurstHeight = 74;
        /// One question in the strip when they are stacked: number, state, one line.
        public const double StripRowHeight = 40;
        /// The card region (the answer, and the CV strip above it) never gets less than this.
        public const double MinimumCardHeight = 160;
        /// Of the answer card at least this much stays in view beside the CV strip.
        public const double MinimumAnswerHeight = 120;
        /// The gap between the CV strip and the answer card under it.
        public const double BriefingGap = Space.Sm;
        /// The CV strip squeezed to its digest line alone.
        public const double BriefingMinimum = 44;
        public const double StripToBar = Space.Inset;
        public const double CardToStrip = 16;
        public const double LabelIndent = Space.Inset;
    }

    /// Content floats straight on the desktop: no cards, pills, outlines or dark tint. Text never fades,
    /// and a soft halo carries it over bright backgrounds. Solid is the way back to filled surfaces.
    public static class Transparency
    {
        /// The level in force, set from the system's transparency setting.
        public static SurfaceTransparency Level { get; set; } = SurfaceTransparency.Balanced;
        public static bool Floating => Level != SurfaceTransparency.Solid;

        /// The first stretch of the slider fades the blur in; the rest darkens it.
        private const double BlurShare = 0.3;

        /// How strong the blur of the desktop is at a background transparency from 0 to 1.
        public static double BlurAlpha(double transparency) => Math.Min(1, (1 - transparency) / BlurShare);

        /// How dark the layer over the blur is at a background transparency from 0 to 1.
        public static double TintAlpha(double transparency) =>
            Math.Max(0, (1 - transparency - BlurShare) / (1 - BlurShare)) * Opacity.O94;
    }

    public static class Waveform
    {
        public const double BarWidth = 2;
        public static readonly double[] BarHeights = { 5, 9, 13, 8, 15, 10, 13, 8, 5 };
        public const double ActiveAlpha = Opacity.O94;
        public const double IdleAlpha = Opacity.O66;
        public const double RestScale = 0.42;
        public const double PeakScaleEven = 1.0;
        public const double PeakScaleOdd = 0.78;
        public const double Period = 0.28;
        public const double PeriodStep = 0.055;
        public const double Stagger = 0.035;
        /// The level meter. A step lasts about as long as the gap between two readings of the level.
        public const double MeterStep = 0.09;
        /// Below `SilentDb` the source is silent; a raised voice reaches `LoudDb`.
        public const double SilentDb = -50;
        public const double LoudDb = -10;
        /// What the bars show while speech is detected and no level has arrived yet.
        public const double SpeakingFloor = 0.6;
        /// How closely each bar follows the level from one reading to the next.
        public static readonly double[] Follow = { 1, 0.62, 0.86, 0.7, 0.94, 0.58, 0.8, 0.66, 0.9, 0.74, 0.98 };
        /// The working dot breathes slowly.
        public const double PulsePeriod = 0.9;
        public const double PulseLowOpacity = 0.4;

        public static Color BarColor(Hue hue, bool animated) => hue.Solid.WithAlpha(animated ? ActiveAlpha : IdleAlpha);
    }

    /// The colours of code. Measured on the Mac for contrast; kept as they are.
    public static class Syntax
    {
        public static readonly Color Keyword = Rgb(0.94, 0.69, 0.95);
        public static readonly Color Type = Rgb(0.91, 0.75, 0.50);
        public static readonly Color String = Rgb(0.66, 0.82, 0.58);
        public static readonly Color Number = Rgb(0.93, 0.73, 0.64);
        public static readonly Color Comment = Rgb(0.75, 0.78, 0.82);
        public static readonly Color Function = Rgb(0.54, 0.80, 0.98);
        public static readonly Color Plain = Rgb(0.87, 0.89, 0.91);
        public static readonly Color Decorator = Rgb(0.91, 0.75, 0.50);

        public static Color Of(SyntaxKind kind) => kind switch
        {
            SyntaxKind.Keyword => Keyword,
            SyntaxKind.Type => Type,
            SyntaxKind.String => String,
            SyntaxKind.Number => Number,
            SyntaxKind.Comment => Comment,
            SyntaxKind.Function => Function,
            SyntaxKind.Decorator => Decorator,
            _ => Plain
        };
    }

    private readonly struct ImmutableSolidColorBrushCache
    {
        private static readonly Dictionary<Color, IBrush> Cache = new();

        public ImmutableSolidColorBrushCache(Color color)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(color, out var brush))
                {
                    brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color);
                    Cache[color] = brush;
                }
                Brush = brush;
            }
        }

        public IBrush Brush { get; }
    }
}
