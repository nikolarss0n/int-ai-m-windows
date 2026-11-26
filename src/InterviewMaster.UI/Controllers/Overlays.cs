using Avalonia.Controls;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

/// Where on the screen a small window of the app's own goes.
public enum OverlayCorner
{
    TopRight
}

/// A borderless window that floats over other apps without taking the keyboard.
public interface IOverlay
{
    void Move(double dx, double dy);
    void Close();
}

/// Opens such windows. The app makes real ones, hidden from screen sharing; the UI driver keeps the
/// content to render it.
public interface IOverlayHost
{
    /// The height of the screen that is free of the taskbar.
    double WorkAreaHeight { get; }

    /// `margin` is the gap to the screen's edges. `movable` lets the person drag it by its background.
    IOverlay Open(Control content, double width, double height, OverlayCorner corner, double margin, bool movable);
}

/// Shows the pinned code on its own while the main window is hidden. A port of
/// `FloatingSolutionWindowController.swift`.
public sealed class FloatingSolutionPresenter : IFloatingSolution
{
    private readonly IOverlayHost host;
    private readonly Func<string?> pinnedSolution;
    private readonly Func<IReadOnlyList<InterviewMessage>> messages;
    private IOverlay? overlay;

    public FloatingSolutionPresenter(IOverlayHost host, Func<string?> pinnedSolution, Func<IReadOnlyList<InterviewMessage>> messages)
    {
        this.host = host;
        this.pinnedSolution = pinnedSolution;
        this.messages = messages;
    }

    public FloatingSolutionView? View { get; private set; }

    public bool IsOpen => overlay is not null;

    public void Show()
    {
        if (pinnedSolution() is not { Length: > 0 } solution) return;
        Dismiss();
        var view = new FloatingSolutionView();
        view.SetSolution(solution);
        view.SetQA(FloatingSolutionView.LastQuestionAnswer(messages()));
        View = view;
        var height = Math.Min(FloatingSolutionView.MaxHeight, host.WorkAreaHeight * 0.8);
        overlay = host.Open(view, FloatingSolutionView.PanelWidth, height, OverlayCorner.TopRight, margin: 20, movable: true);
    }

    public void UpdateContent(string content) => View?.SetSolution(content);

    public void UpdateQA() => View?.SetQA(FloatingSolutionView.LastQuestionAnswer(messages()));

    public void ShowLoading() => View?.SetLoading(true);

    public void HideLoading() => View?.SetLoading(false);

    public void Dismiss()
    {
        overlay?.Close();
        overlay = null;
        View = null;
    }

    /// The keys that reach the floating code from any app: J and K scroll it, the arrows move it,
    /// backslash puts it away.
    /// Returns false when it is not open or the key is not one of its own.
    public bool HandleKey(ShortcutKey key)
    {
        if (overlay is null || View is null) return false;
        switch (key)
        {
            case ShortcutKey.J: View.Scroll(-FloatingSolutionView.ScrollStep); break;
            case ShortcutKey.K: View.Scroll(FloatingSolutionView.ScrollStep); break;
            case ShortcutKey.Up: overlay.Move(0, -FloatingSolutionView.MoveStep); break;
            case ShortcutKey.Down: overlay.Move(0, FloatingSolutionView.MoveStep); break;
            case ShortcutKey.Left: overlay.Move(-FloatingSolutionView.MoveStep, 0); break;
            case ShortcutKey.Right: overlay.Move(FloatingSolutionView.MoveStep, 0); break;
            case ShortcutKey.Backslash: Dismiss(); break;
            default: return false;
        }
        return true;
    }
}

/// Shows the screenshot note at the corner of the screen. A port of `ScreenshotAlertWindow.swift`.
public sealed class ScreenshotAlertPresenter
{
    private readonly IOverlayHost host;
    private IOverlay? overlay;

    public ScreenshotAlertPresenter(IOverlayHost host) => this.host = host;

    public ScreenshotAlertView? View { get; private set; }

    public void Show(IReadOnlyList<Screenshot> screenshots)
    {
        if (overlay is null || View is null)
        {
            View = new ScreenshotAlertView();
            overlay = host.Open(View, ScreenshotAlertView.AlertWidth, ScreenshotAlertView.AlertHeight, OverlayCorner.TopRight, margin: 20, movable: false);
        }
        View.Show(screenshots);
    }

    public void Hide()
    {
        overlay?.Close();
        overlay = null;
        View = null;
    }
}
