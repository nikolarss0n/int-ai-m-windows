using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Presentation;

/// The real window and the desktop around it: dialogs, small windows, the floating overlays, and
/// what the main window does when it is shown, hidden, moved or told to let clicks through.
public sealed class DesktopShell : IAppShell, IOverlayHost
{
    private const double FadeSeconds = 0.2;

    private readonly MainWindow window;
    private readonly INativeWindows native;
    private readonly List<Window> panels = new();
    private readonly HashSet<Window> guarded = new();
    private readonly TextBlock announcer = new() { IsHitTestVisible = false, Opacity = 0, Width = 1, Height = 1 };
    private int modals;
    private int fadeGeneration;
    private bool clickThrough;

    public DesktopShell(MainWindow window, INativeWindows native)
    {
        this.window = window;
        this.native = native;
        ScreenshotAlert = new ScreenshotAlertPresenter(this);
        window.View.Children.Add(announcer);
        window.Opened += (_, _) => ApplyNative();
    }

    /// The window came or went, a dialog opened or closed, or the keyboard changed hands.
    public event Action? Changed;

    public Action? OpenSettings { get; set; }

    public ScreenshotAlertPresenter ScreenshotAlert { get; }

    /// The dialog on screen, for the UI driver to answer.
    public AlertView? PresentedAlert { get; private set; }

    public IReadOnlyList<Window> Panels => panels;

    /// The overlays that are open, newest last.
    public List<Window> Overlays { get; } = new();

    public bool IsModalOpen => modals > 0;

    /// Practice lets the window take the keyboard; everywhere else it never does.
    public bool AcceptsKeyboard { get; private set; }

    public bool IsWindowVisible => window.IsVisible;

    public bool IsWindowActive => window.IsActive;

    public IFloatingSolution? FloatingSolution { get; set; }

    // MARK: - Questions and file dialogs

    public async Task<int> AskAsync(AppQuestion question)
    {
        var view = new AlertView(question);
        var dialog = new Window
        {
            Title = "Interview Master",
            Width = AlertView.PanelWidth,
            Height = view.PanelHeight,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            Topmost = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Token.Colors.BgSolidWindow.Brush(),
            Content = view,
        };
        var result = question.DismissIndex;
        view.Chosen += index =>
        {
            result = index;
            dialog.Close();
        };
        dialog.Opened += (_, _) => view.Focus();
        Guard(dialog, neverActivates: false);

        modals++;
        PresentedAlert = view;
        Changed?.Invoke();
        try
        {
            if (Owner() is { } owner)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                var closed = new TaskCompletionSource();
                dialog.Closed += (_, _) => closed.TrySetResult();
                dialog.Show();
                await closed.Task;
            }
        }
        finally
        {
            modals--;
            if (ReferenceEquals(PresentedAlert, view)) PresentedAlert = null;
            Changed?.Invoke();
        }
        return result;
    }

    public Task<string?> ChooseExportFileAsync(string suggestedName) => SaveAsync(
        "Export Interview", suggestedName, new FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } },
        new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } });

    public Task<string?> ChooseSaveFileAsync(string title, string suggestedName) => SaveAsync(
        title, suggestedName, new FilePickerFileType("Text") { Patterns = new[] { "*" + Path.GetExtension(suggestedName), "*.txt" } });

    private Task<string?> SaveAsync(string title, string suggestedName, params FilePickerFileType[] types) => Modal(async top =>
    {
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = Path.GetExtension(suggestedName).TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = types,
        });
        return file?.TryGetLocalPath();
    });

    public Task<string?> ChooseOpenFileAsync(string title, IReadOnlyList<string> extensions) => Modal(async top =>
    {
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(title) { Patterns = extensions.Select(extension => "*." + extension).ToArray() } },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    });

    private async Task<string?> Modal(Func<TopLevel, Task<string?>> ask)
    {
        modals++;
        Changed?.Invoke();
        try
        {
            return await ask(Owner() ?? window);
        }
        catch (Exception error)
        {
            Log.Print($"⚠️ File dialog: {error.Message}");
            return null;
        }
        finally
        {
            modals--;
            Changed?.Invoke();
        }
    }

    /// The window a dialog belongs to: the small window in front, or the main one while it is up.
    private Window? Owner() => panels.LastOrDefault(panel => panel.IsVisible) ?? (window.IsVisible ? window : null);

    // MARK: - Small windows

    public IPanel ShowPanel(string title, Control content, double width, double height)
    {
        var panel = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            Topmost = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Token.Colors.BgSolidWindow.Brush(),
            Content = content,
        };
        panels.Add(panel);
        panel.Closed += (_, _) =>
        {
            panels.Remove(panel);
            panel.Content = null;
        };
        Guard(panel, neverActivates: false);
        panel.Show();
        panel.Activate();
        return new OpenWindow(panel);
    }

    public void ShowSettings() => OpenSettings?.Invoke();

    private sealed class OpenWindow(Window window) : IPanel, IOverlay
    {
        private bool closed;

        public void Move(double dx, double dy)
        {
            if (closed) return;
            var scaling = window.DesktopScaling;
            window.Position += new PixelPoint((int)Math.Round(dx * scaling), (int)Math.Round(dy * scaling));
        }

        public void Close()
        {
            if (closed) return;
            closed = true;
            window.Close();
        }
    }

    // MARK: - Overlays

    public double WorkAreaHeight
    {
        get
        {
            var screen = window.Screens.Primary;
            return screen is null ? 860 : screen.WorkingArea.Height / screen.Scaling;
        }
    }

    public IOverlay Open(Control content, double width, double height, OverlayCorner corner, double margin, bool movable)
    {
        var overlay = new Window
        {
            Title = "Interview Master",
            Width = width,
            Height = height,
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = content,
        };
        if (window.Screens.Primary is { } screen)
        {
            var area = screen.WorkingArea;
            overlay.Position = new PixelPoint(
                area.Right - (int)Math.Round((width + margin) * screen.Scaling), area.Y + (int)Math.Round(margin * screen.Scaling));
        }
        if (movable) WindowDrag.MoveByBackground(overlay);
        Overlays.Add(overlay);
        overlay.Closed += (_, _) =>
        {
            Overlays.Remove(overlay);
            overlay.Content = null;
        };
        Guard(overlay, neverActivates: true);
        overlay.Show();
        return new OpenWindow(overlay);
    }

    public void ShowScreenshotAlert(IReadOnlyList<Screenshot> screenshots) => ScreenshotAlert.Show(screenshots);

    public void HideScreenshotAlert() => ScreenshotAlert.Hide();

    // MARK: - The main window

    public void ShowWindow(bool animated)
    {
        var generation = ++fadeGeneration;
        var fades = animated && !Token.Motion.ReducesMotion();
        window.View.Transitions = null;
        window.View.Opacity = fades ? 0 : 1;
        if (!window.IsVisible) window.Show();
        ApplyNative();
        if (AcceptsKeyboard) window.Activate();
        if (fades) FadeTo(1, generation, null);
        Changed?.Invoke();
    }

    public void HideWindow(bool animated)
    {
        var generation = ++fadeGeneration;
        if (!window.IsVisible) return;
        void Finish()
        {
            window.Hide();
            window.View.Transitions = null;
            window.View.Opacity = 1;
            Changed?.Invoke();
        }
        if (animated && !Token.Motion.ReducesMotion()) FadeTo(0, generation, Finish);
        else Finish();
    }

    private void FadeTo(double opacity, int generation, Action? done)
    {
        window.View.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromSeconds(FadeSeconds) },
        };
        window.View.Opacity = opacity;
        Delayed.After(FadeSeconds, () =>
        {
            // A later show or hide took over while this one was under way.
            if (generation == fadeGeneration) done?.Invoke();
        });
    }

    public void SetClickThrough(bool on)
    {
        clickThrough = on;
        if (Handle(window) is var handle && handle != IntPtr.Zero) native.SetClickThrough(handle, on);
    }

    /// Practice needs the keyboard: the window may be activated and typed into while this is on.
    public void SetAcceptsKeyboard(bool on)
    {
        if (AcceptsKeyboard == on) return;
        AcceptsKeyboard = on;
        if (Handle(window) is var handle && handle != IntPtr.Zero) native.SetNeverActivates(handle, !on);
        if (on)
        {
            if (window.IsVisible) window.Activate();
        }
        else
        {
            // Takes the caret out of the answer field; the window itself holds no text.
            window.Focus();
        }
        Changed?.Invoke();
    }

    /// Puts the caret in a Practice field. Ignored unless the window may take the keyboard.
    public void FocusInput(Control? control)
    {
        if (!AcceptsKeyboard || !window.IsVisible) return;
        window.Activate();
        control?.Focus();
    }

    /// Moves the window by a step. It can cross to another display but not leave them all.
    public void NudgeWindow(double dx, double dy)
    {
        var scaling = window.DesktopScaling;
        var x = window.Position.X + (int)Math.Round(dx * scaling);
        var y = window.Position.Y + (int)Math.Round(dy * scaling);
        var areas = window.Screens.All.Select(screen => screen.WorkingArea).ToList();
        if (areas.Count > 0)
        {
            var visible = areas.Skip(1).Aggregate(areas[0], (union, area) => union.Union(area));
            var size = PixelSize.FromSize(window.FrameSize ?? window.ClientSize, scaling);
            x = Math.Min(Math.Max(x, visible.X), Math.Max(visible.X, visible.Right - size.Width));
            y = Math.Min(Math.Max(y, visible.Y), Math.Max(visible.Y, visible.Bottom - size.Height));
        }
        window.Position = new PixelPoint(x, y);
    }

    // MARK: - The desktop

    public void RevealFile(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));
            else if (OperatingSystem.IsMacOS()) Process.Start("open", new[] { "-R", path });
            else if (Path.GetDirectoryName(path) is { } folder) _ = window.Launcher.LaunchUriAsync(new Uri(folder));
        }
        catch (Exception error)
        {
            Log.Print($"⚠️ Could not show the file: {error.Message}");
        }
    }

    public void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var link)) return;
        _ = window.Launcher.LaunchUriAsync(link);
    }

    /// Says `text` to a screen reader through a live region: the text itself is never on screen.
    public void Announce(string text, bool urgent)
    {
        AutomationProperties.SetLiveSetting(announcer, urgent ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        // The same words twice in a row are still two announcements.
        announcer.Text = announcer.Text == text ? text + "​" : text;
    }

    // MARK: - Native window settings

    /// The system's own name for a window. The UI driver replaces it, since a headless window has none.
    public static Func<Window, IntPtr> HandleOf { get; set; } = target => target.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    private static IntPtr Handle(Window target) => HandleOf(target);

    /// Keeps a window of the app out of screen shares. Done before it is first shown where the
    /// system already has a window to speak of, and when it opens otherwise.
    private void Guard(Window target, bool neverActivates)
    {
        void Apply()
        {
            var handle = Handle(target);
            if (handle == IntPtr.Zero || !guarded.Add(target)) return;
            native.ExcludeFromCapture(handle);
            if (!neverActivates) return;
            native.HideFromTaskSwitcher(handle);
            native.SetNeverActivates(handle, true);
        }
        Apply();
        target.Opened += (_, _) => Apply();
        target.Closed += (_, _) => guarded.Remove(target);
    }

    private void ApplyNative()
    {
        var handle = Handle(window);
        if (handle == IntPtr.Zero) return;
        if (guarded.Add(window))
        {
            native.ExcludeFromCapture(handle);
            native.HideFromTaskSwitcher(handle);
        }
        native.SetNeverActivates(handle, !AcceptsKeyboard);
        native.SetClickThrough(handle, clickThrough);
    }
}
