using InterviewMaster.Domain;
using InterviewMaster.Presentation;

namespace InterviewMaster.UiDriver;

/// The window and the desktop, as far as a headless run has them: the window is up, nothing is
/// asked, and what the controller wanted to do is written down.
public sealed class DriverShell : IAppShell, IOverlayHost
{
    public List<string> Calls { get; } = new();

    public bool IsWindowVisible => true;

    public bool IsWindowActive => true;

    public IFloatingSolution? FloatingSolution => null;

    /// The button the next question is answered with; without one, the last button.
    public string? NextAnswer { get; set; }

    public Task<int> AskAsync(AppQuestion question)
    {
        var chosen = NextAnswer is { } wanted ? question.Buttons.ToList().IndexOf(wanted) : -1;
        NextAnswer = null;
        if (chosen < 0) chosen = question.Buttons.Count - 1;
        Calls.Add($"ask: {question.Title} [{string.Join(" | ", question.Buttons)}] -> {question.Buttons[chosen]}");
        return Task.FromResult(chosen);
    }

    public Task<string?> ChooseExportFileAsync(string suggestedName)
    {
        Calls.Add($"export: {suggestedName}");
        return Task.FromResult<string?>(null);
    }

    /// Where the next save dialog points; null cancels it.
    public string? SaveFile { get; set; }

    /// What the last panel showed.
    public Avalonia.Controls.Control? PanelContent { get; private set; }

    public Avalonia.Size PanelSize { get; private set; }

    public Task<string?> ChooseSaveFileAsync(string title, string suggestedName)
    {
        Calls.Add($"save: {title} ({suggestedName})");
        return Task.FromResult(SaveFile);
    }

    /// The file the next open dialog returns; null cancels it.
    public string? OpenFile { get; set; }

    public Task<string?> ChooseOpenFileAsync(string title, IReadOnlyList<string> extensions)
    {
        Calls.Add($"open: {title} ({string.Join(", ", extensions)})");
        return Task.FromResult(OpenFile);
    }

    public IPanel ShowPanel(string title, Avalonia.Controls.Control content, double width, double height)
    {
        Calls.Add($"panel: {title} {width}x{height}");
        PanelContent = content;
        PanelSize = new Avalonia.Size(width, height);
        return new Panel(this);
    }

    public double WorkAreaHeight => 860;

    public IOverlay Open(Avalonia.Controls.Control content, double width, double height, OverlayCorner corner, double margin, bool movable)
    {
        Calls.Add($"overlay: {width}x{height} {corner} movable={movable}");
        PanelContent = content;
        PanelSize = new Avalonia.Size(width, height);
        return new Overlay(this);
    }

    private sealed class Overlay(DriverShell shell) : IOverlay
    {
        public void Move(double dx, double dy) => shell.Calls.Add($"overlay moved: {dx},{dy}");

        public void Close()
        {
            shell.Calls.Add("overlay closed");
            shell.PanelContent = null;
        }
    }

    private sealed class Panel(DriverShell shell) : IPanel
    {
        public void Close()
        {
            shell.Calls.Add("panel closed");
            shell.PanelContent = null;
        }
    }

    public void ShowSettings() => Calls.Add("settings");
    public void RevealFile(string path) => Calls.Add("reveal");
    public void OpenLink(string url) => Calls.Add($"link: {url}");
    public void Announce(string text, bool urgent) => Calls.Add($"announce{(urgent ? "!" : "")}: {text}");
    public void SetClickThrough(bool on) => Calls.Add($"clickThrough: {on}");
    public void ShowWindow(bool animated) => Calls.Add("show");
    public void HideWindow(bool animated) => Calls.Add("hide");
    public void NudgeWindow(double dx, double dy) => Calls.Add($"nudge: {dx},{dy}");
    public void ShowScreenshotAlert(IReadOnlyList<Screenshot> screenshots) => Calls.Add($"screenshotAlert: {screenshots.Count}");
    public void HideScreenshotAlert() => Calls.Add("hideScreenshotAlert");
}
