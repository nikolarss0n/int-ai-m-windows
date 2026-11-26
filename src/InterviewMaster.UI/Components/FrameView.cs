using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace InterviewMaster.Presentation;

/// A panel whose children sit at frames the view works out itself, as the Mac views do. Subclasses
/// place each visible child in `LayoutFrames`; a child that is not placed takes no room.
public abstract class FrameView : Panel
{
    private readonly Dictionary<Control, Rect> frames = new();
    private Size laidOut = new(-1, -1);

    protected abstract void LayoutFrames(Size size);

    /// The size the view asks for when its parent does not dictate one.
    protected virtual Size PreferredSize(Size available) => available;

    /// Where a child was last placed, in this view's coordinates.
    public Rect FrameOf(Control child) => frames.TryGetValue(child, out var frame) ? frame : default;

    protected void Place(Control child, double x, double y, double width, double height)
    {
        var frame = new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
        frames[child] = frame;
        child.Measure(frame.Size);
    }

    protected void Place(Control child, Rect frame) => Place(child, frame.X, frame.Y, frame.Width, frame.Height);

    /// The height `label` needs at `width`, within its own line limit.
    protected static double HeightOf(Control label, double width)
    {
        label.Measure(new Size(Math.Max(0, width), double.PositiveInfinity));
        return Math.Ceiling(label.DesiredSize.Height);
    }

    protected static double WidthOf(Control label)
    {
        label.Measure(Size.Infinity);
        return Math.Ceiling(label.DesiredSize.Width);
    }

    public void Relayout()
    {
        InvalidateMeasure();
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = PreferredSize(new Size(Finite(availableSize.Width), Finite(availableSize.Height)));
        Run(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize != laidOut) Run(finalSize);
        foreach (var child in Children)
        {
            child.Arrange(child.IsVisible && frames.TryGetValue(child, out var frame) ? frame : default);
        }
        return finalSize;
    }

    private void Run(Size size)
    {
        frames.Clear();
        laidOut = size;
        LayoutFrames(size);
    }

    private static double Finite(double value) => double.IsInfinity(value) || double.IsNaN(value) ? 0 : value;
}

/// One clock for everything that moves, so a hidden view costs nothing and all pulses stay in step.
public static class FrameClock
{
    private static readonly List<Action<double>> Subscribers = new();
    private static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
    private static DispatcherTimer? timer;

    /// Seconds since the app started. The UI driver replaces it to render a chosen moment.
    public static Func<double> Now { get; set; } = () => Watch.Elapsed.TotalSeconds;

    public static IDisposable Subscribe(Action<double> tick)
    {
        Subscribers.Add(tick);
        timer ??= new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 30), DispatcherPriority.Render, (_, _) => Tick());
        timer.Start();
        return new Subscription(tick);
    }

    private static void Tick()
    {
        var now = Now();
        foreach (var subscriber in Subscribers.ToArray()) subscriber(now);
    }

    private sealed class Subscription(Action<double> tick) : IDisposable
    {
        public void Dispose()
        {
            Subscribers.Remove(tick);
            if (Subscribers.Count == 0) timer?.Stop();
        }
    }
}

/// A control that redraws on the frame clock while it is on screen and has something to move.
public abstract class AnimatedControl : Control
{
    private IDisposable? subscription;
    private bool attached;

    protected abstract bool WantsFrames { get; }

    protected virtual void OnFrame(double now)
    {
        if (IsEffectivelyVisible) InvalidateVisual();
    }

    protected void RefreshClock()
    {
        var wanted = WantsFrames && attached && !Token.Motion.ReducesMotion();
        if (wanted && subscription is null) subscription = FrameClock.Subscribe(OnFrame);
        if (!wanted && subscription is not null)
        {
            subscription.Dispose();
            subscription = null;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        RefreshClock();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        attached = false;
        RefreshClock();
    }
}
