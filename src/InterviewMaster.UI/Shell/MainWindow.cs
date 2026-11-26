using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace InterviewMaster.Presentation;

/// The app's one main window: a dark glass panel above every other window, with no title bar, out of
/// the taskbar, shown without taking the keyboard. A port of `WindowFactory.createMainWindow`.
public sealed class MainWindow : Window
{
    public const double DefaultWidth = 760;
    public const double DefaultHeight = 500;

    public MainWindow()
    {
        Title = "Interview Master";
        Width = DefaultWidth;
        Height = DefaultHeight;
        MinWidth = DefaultWidth;
        MinHeight = DefaultHeight;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(100, 100);
        View.BlurStandIn = true;
        View.ApplyTransparency(0.7);
        Content = View;
        WindowDrag.ResizeByEdges(this);
        WindowDrag.MoveByBackground(this);
    }

    public MainView View { get; } = new();
}
