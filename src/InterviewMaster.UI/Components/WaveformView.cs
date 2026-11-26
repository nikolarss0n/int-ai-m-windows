using Avalonia;
using Avalonia.Media;

namespace InterviewMaster.Presentation;

/// The bars beside a status. They move on their own while something is in progress, and follow the
/// loudness of the source while it is listened to.
public sealed class WaveformView : AnimatedControl
{
    private readonly double[] shown = new double[Token.Waveform.BarHeights.Length];
    private readonly double[] from = new double[Token.Waveform.BarHeights.Length];
    private readonly double[] target = new double[Token.Waveform.BarHeights.Length];
    private Token.Hue hue = Token.Hue.Neutral;
    private bool animated;
    private bool meter;
    private bool lit;
    private double easeStart = double.NegativeInfinity;
    private double activityStart;
    private int meterTick;

    public WaveformView()
    {
        IsHitTestVisible = false;
        Array.Fill(shown, 1);
        Array.Fill(target, 1);
    }

    /// The loudness shown, 0 to 1.
    public double Level { get; private set; }

    /// Whether detected speech holds the bars up while no level has arrived.
    public bool Speaking { get; set; }

    public bool IsMeter => meter;

    protected override bool WantsFrames => animated || (meter && FrameClock.Now() - easeStart < Token.Waveform.MeterStep * 2);

    public void Set(Token.Hue next, bool animate, bool asMeter)
    {
        var unchanged = animated == animate && meter == asMeter && hue == next;
        hue = next;
        animated = animate;
        meter = asMeter;
        if (!asMeter) Level = 0;
        if (!unchanged) activityStart = FrameClock.Now();
        if (asMeter) RenderMeter(eased: unchanged);
        RefreshClock();
        InvalidateVisual();
    }

    /// Returns whether the bars changed.
    public bool SetLevel(double db)
    {
        var span = Token.Waveform.LoudDb - Token.Waveform.SilentDb;
        var heard = Math.Clamp((db - Token.Waveform.SilentDb) / span, 0, 1);
        if (heard <= 0 && Level <= 0) return false;
        Level = heard;
        if (!meter || !IsEffectivelyVisible || Token.Motion.ReducesMotion()) return false;
        meterTick++;
        RenderMeter(eased: true);
        return true;
    }

    private void RenderMeter(bool eased)
    {
        var still = Token.Motion.ReducesMotion();
        var floor = Speaking ? Token.Waveform.SpeakingFloor : 0;
        var level = still ? floor : Math.Max(Level, floor);
        lit = level > 0;
        var rest = Token.Waveform.RestScale;
        for (var index = 0; index < target.Length; index++)
        {
            var follow = still ? 1 : Token.Waveform.Follow[(index * 7 + meterTick * 3) % Token.Waveform.Follow.Length];
            from[index] = shown[index];
            target[index] = rest + (1 - rest) * level * follow;
            if (!eased || still) shown[index] = target[index];
        }
        easeStart = eased && !still ? FrameClock.Now() : double.NegativeInfinity;
        RefreshClock();
        InvalidateVisual();
    }

    protected override void OnFrame(double now)
    {
        base.OnFrame(now);
        if (!animated) RefreshClock();
    }

    private double ScaleOf(int index, double now)
    {
        if (meter)
        {
            var progress = Math.Clamp((now - easeStart) / Token.Waveform.MeterStep, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            shown[index] = from[index] + (target[index] - from[index]) * eased;
            return shown[index];
        }
        if (!animated || Token.Motion.ReducesMotion()) return 1;
        var duration = Token.Waveform.Period + index % 4 * Token.Waveform.PeriodStep;
        var elapsed = now - activityStart - index * Token.Waveform.Stagger;
        if (elapsed < 0) return Token.Waveform.RestScale;
        var phase = elapsed % (duration * 2) / duration;
        var along = phase <= 1 ? phase : 2 - phase;
        var ease = along * along * (3 - 2 * along);
        var peak = index % 2 == 0 ? Token.Waveform.PeakScaleEven : Token.Waveform.PeakScaleOdd;
        return Token.Waveform.RestScale + (peak - Token.Waveform.RestScale) * ease;
    }

    public override void Render(DrawingContext context)
    {
        var heights = Token.Waveform.BarHeights;
        if (Bounds.Width <= 0) return;
        var barWidth = Token.Waveform.BarWidth;
        var spacing = Math.Max(1.5, (Bounds.Width - heights.Length * barWidth) / (heights.Length - 1));
        var brush = Token.Waveform.BarColor(hue, meter ? lit : animated).Brush();
        var now = FrameClock.Now();
        var radius = Token.Radius.Full(barWidth);
        for (var index = 0; index < heights.Length; index++)
        {
            var height = Math.Min(Bounds.Height, heights[index]) * ScaleOf(index, now);
            var rect = new Rect(index * (barWidth + spacing), (Bounds.Height - height) / 2, barWidth, height);
            context.DrawRectangle(brush, null, rect, radius, radius);
        }
    }
}
