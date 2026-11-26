using Avalonia;
using Avalonia.Controls;
using InterviewMaster.Infrastructure;
using InterviewMaster.Presentation;
using InterviewMaster.Support;

namespace InterviewMaster.App;

internal static class Program
{
    private static readonly object LogGate = new();

    [STAThread]
    public static int Main(string[] args)
    {
        // A second copy would add a second tray icon and hear every shortcut twice.
        using var single = new Mutex(initiallyOwned: true, @"Local\InterviewMaster.SingleInstance", out var isFirst);
        if (!isFirst) return 0;

        StartLogs();
        try
        {
            SettingsStore.Standard = new JsonFileSettingsStore();
            _ = MemoryStore.Shared;
            InterviewMasterApp.Platform = () => new WindowsPlatform();
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception error)
        {
            Log.Print($"💥 The app stopped: {error}");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<InterviewMasterApp>().UsePlatformDetect();

    /// The macOS app writes to the console and to a log in its working folder. A Windows app has no
    /// console and may sit in a folder it cannot write to, so both logs go under the user's profile.
    private static void StartLogs()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            var appLog = Path.Combine(AppPaths.LogsDirectory, "app.log");
            File.WriteAllText(appLog, "");
            Log.Sink = line =>
            {
                try
                {
                    lock (LogGate) File.AppendAllText(appLog, $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                }
            };
            DebugLogger.Shared = new DebugLogger(Path.Combine(AppPaths.LogsDirectory, "interview_debug.log"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        DebugLogger.Shared.Clear();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Print($"💥 Unhandled: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Print($"⚠️ Unobserved task error: {e.Exception}");
    }
}
