using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace InterviewMaster.Presentation;

/// The running app: a dark theme, the main window, the tray icon, and no taskbar button. It keeps
/// running with its window hidden until Quit is chosen.
public sealed class InterviewMasterApp : Application
{
    private AppHost? host;
    private Tray? tray;

    /// Set before the app starts: what this operating system offers.
    public static Func<IPlatformServices>? Platform { get; set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Platform is { } platform)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow { Icon = AppIcon.Make(256) };
            var app = new AppHost(window, platform(), () => desktop.Shutdown());
            host = app;
            tray = new Tray(app);
            desktop.ShutdownRequested += (_, e) =>
            {
                // Signing out or shutting down: an unfinished Practice session is asked about first.
                if (app.IsQuitting || !app.Practice.HasActiveSession) return;
                e.Cancel = true;
                _ = app.QuitAsync();
            };
            desktop.Exit += (_, _) =>
            {
                app.Shutdown();
                tray?.Dispose();
            };
            app.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
