using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// Everything in the main window: the dark layer that carries the text over the desktop, the view of
/// the current tab, and the command bar under it.
public sealed class MainView : FrameView
{
    private readonly Border backdrop = new() { IsHitTestVisible = false };
    private readonly Dictionary<Tab, Control> tabViews = new();
    private Tab currentTab = Tab.Voice;

    public MainView()
    {
        Children.Add(backdrop);
        Children.Add(Interview);
        Children.Add(Bar);
        tabViews[Tab.Voice] = Interview;
        ApplyTransparency(0.7);
    }

    public InterviewFocusView Interview { get; } = new();

    public CommandBarView Bar { get; } = new();

    public Tab CurrentTab => currentTab;

    /// Adds the view a tab shows. The live view is built in.
    public void SetTabView(Tab tab, Control view)
    {
        if (tabViews.TryGetValue(tab, out var existing) && existing != view) Children.Remove(existing);
        tabViews[tab] = view;
        view.IsVisible = tab == currentTab;
        if (!Children.Contains(view)) Children.Insert(Children.IndexOf(Bar), view);
        Relayout();
    }

    public void ShowTab(Tab tab)
    {
        currentTab = tab;
        foreach (var (key, view) in tabViews) view.IsVisible = key == tab;
        // Practice brings its own controls; the bar would only be in its way.
        Bar.IsVisible = tab != Tab.Practice;
        Relayout();
    }

    /// On macOS the desktop behind the window is blurred, and the slider fades that blur. A Windows
    /// window cannot fade a blur, so on the desktop a dark layer of the same strength stands in for it.
    public bool BlurStandIn { get; set; }

    /// How dark the stand-in is where the macOS blur is at full strength.
    private const double BlurStandInAlpha = 0.72;

    /// The halo that carries floating text over a bright desktop, and the dark layer the background
    /// slider strengthens. `transparency` runs from 0 (dark) to 1 (clear).
    public void ApplyTransparency(double transparency)
    {
        var floating = Token.Transparency.Floating;
        var tint = Token.Transparency.TintAlpha(transparency);
        var glass = BlurStandIn ? Token.Transparency.BlurAlpha(transparency) * BlurStandInAlpha : 0;
        backdrop.Background = floating
            ? Token.Colors.BgGlassFloor(1 - (1 - glass) * (1 - tint)).Brush()
            : Token.Colors.BgSolidWindow.Brush();
        backdrop.CornerRadius = new CornerRadius(Token.Radius.Lg);
        var halo = Token.Elevation.TextHalo;
        foreach (var view in Children.Where(child => child != backdrop))
        {
            view.Effect = floating
                ? new DropShadowEffect { Color = halo.Color, Opacity = halo.Opacity, BlurRadius = halo.Radius * 2, OffsetX = halo.OffsetX, OffsetY = halo.OffsetY }
                : null;
        }
        Interview.Answer.ApplyTransparency();
        Interview.Briefing.ApplyTransparency();
        Interview.Strip.ApplyTransparency();
        Interview.CodePanel.ApplyTransparency();
        Bar.ApplyTransparency();
    }

    protected override void LayoutFrames(Size size)
    {
        Place(backdrop, 0, 0, size.Width, size.Height);
        var barTop = size.Height - Token.CommandBar.BottomMargin - Token.CommandBar.Height;
        Interview.BarTop = barTop;
        foreach (var view in tabViews.Values.Where(view => view.IsVisible)) Place(view, 0, 0, size.Width, size.Height);
        if (Bar.IsVisible) Place(Bar, Token.CommandBar.SideMargin, barTop, size.Width - Token.CommandBar.SideMargin * 2, Token.CommandBar.Height);
    }
}
