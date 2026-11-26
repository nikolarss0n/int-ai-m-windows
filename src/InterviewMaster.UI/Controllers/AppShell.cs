using Avalonia.Threading;

namespace InterviewMaster.Presentation;

/// A question the app has to stop and ask. The first button is the default. `Dismiss` is the button
/// that closing the dialog stands for; without one it is the last.
public sealed record AppQuestion(string Title, string Message, IReadOnlyList<string> Buttons, bool Critical = false, int? Dismiss = null)
{
    public int DismissIndex => Dismiss ?? Buttons.Count - 1;

    public static AppQuestion Notice(string title, string message, bool critical = false) =>
        new(title, message, new[] { "OK" }, critical);
}

/// The code overlay that floats on its own while the main window is hidden.
public interface IFloatingSolution
{
    bool IsOpen { get; }
    void Show();
    void UpdateContent(string content);
    void UpdateQA();
    void ShowLoading();
    void HideLoading();
    void Dismiss();
}

/// A small window the app opened and can close again.
public interface IPanel
{
    void Close();
}

/// What the interview controller needs from the window and the desktop around it. The app supplies
/// the real one; the UI driver supplies one that answers from a script.
public interface IAppShell
{
    bool IsWindowVisible { get; }

    /// Whether the main window has the keyboard.
    bool IsWindowActive { get; }

    IFloatingSolution? FloatingSolution { get; }

    /// Asks and returns the index of the chosen button. Closing the dialog is the last button.
    Task<int> AskAsync(AppQuestion question);

    /// Where to export to, or null when the person cancels. The extension picks the format.
    Task<string?> ChooseExportFileAsync(string suggestedName);

    /// Where to save a text file, or null when the person cancels.
    Task<string?> ChooseSaveFileAsync(string title, string suggestedName);

    /// A file to read, or null when the person cancels. `extensions` are without the dot.
    Task<string?> ChooseOpenFileAsync(string title, IReadOnlyList<string> extensions);

    /// Opens a small window of its own around `content`, above the other windows.
    IPanel ShowPanel(string title, Avalonia.Controls.Control content, double width, double height);

    void ShowSettings();
    void RevealFile(string path);
    void OpenLink(string url);
    void Announce(string text, bool urgent);
    void SetClickThrough(bool on);
    void ShowWindow(bool animated);
    void HideWindow(bool animated);

    /// Moves the window by a step, keeping it on a screen.
    void NudgeWindow(double dx, double dy);

    /// New screenshots were taken while the window did not have the keyboard.
    void ShowScreenshotAlert(IReadOnlyList<Domain.Screenshot> screenshots);

    void HideScreenshotAlert();
}

/// Work that runs once, later, on the UI thread, and can be called off.
public sealed class Delayed : IDisposable
{
    private readonly DispatcherTimer timer;

    private Delayed(double seconds, Action work)
    {
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(0.01, seconds)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            work();
        };
        timer.Start();
    }

    public static Delayed After(double seconds, Action work) => new(seconds, work);

    public void Dispose() => timer.Stop();
}
