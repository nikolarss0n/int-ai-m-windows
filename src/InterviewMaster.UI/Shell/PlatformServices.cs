using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Presentation;

/// What Avalonia does not offer for a window. Each call takes the window's native handle.
public interface INativeWindows
{
    /// Keeps the window out of screen shares, recordings and screenshots.
    void ExcludeFromCapture(IntPtr window);

    /// Clicks pass through to the app behind.
    void SetClickThrough(IntPtr window, bool on);

    /// The window never takes the keyboard from the app in front.
    void SetNeverActivates(IntPtr window, bool on);

    /// Out of the taskbar and the task switcher.
    void HideFromTaskSwitcher(IntPtr window);
}

/// Everything the app needs from the operating system. The Windows app supplies the real ones; the
/// UI driver supplies stand-ins so the same wiring runs without a device.
public interface IPlatformServices
{
    INativeWindows Windows { get; }

    IScreenCapture? ScreenCapture { get; }

    bool HasAudioPermission(CaptureDevice device);

    Task PreflightAudioAsync(CaptureDevice device, bool allowPrompt);

    IAudioSourceCapture MakeAudioCapture(CaptureDevice device, IMainThread mainThread);

    /// Null when spoken practice answers cannot be recorded.
    IPracticeRecorder? MakePracticeRecorder();

    /// Null when the keyboard cannot be heard from other apps.
    IGlobalKeys? MakeGlobalKeys(IMainThread mainThread, Action<ShortcutAction> perform);
}
