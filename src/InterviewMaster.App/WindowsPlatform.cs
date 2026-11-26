using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Platform.Windows.Audio;
using InterviewMaster.Platform.Windows.Input;
using InterviewMaster.Platform.Windows.Screen;
using InterviewMaster.Platform.Windows.Window;
using InterviewMaster.Presentation;
using InterviewMaster.Support;

namespace InterviewMaster.App;

/// What Windows offers the app: WASAPI capture, the keyboard hook, GDI screenshots and the window
/// styles that keep it out of screen shares and away from the keyboard.
internal sealed class WindowsPlatform : IPlatformServices, INativeWindows
{
    public INativeWindows Windows => this;

    public IScreenCapture? ScreenCapture { get; } = new GdiScreenCapture();

    public bool HasAudioPermission(CaptureDevice device) => WindowsAudioCapture.HasPermission(device);

    public Task PreflightAudioAsync(CaptureDevice device, bool allowPrompt) => WindowsAudioCapture.PreflightAsync(device, allowPrompt);

    public IAudioSourceCapture MakeAudioCapture(CaptureDevice device, IMainThread mainThread) => WindowsAudioCapture.Make(device, mainThread);

    public IPracticeRecorder? MakePracticeRecorder() => WindowsAudioCapture.MakePracticeRecorder();

    public IGlobalKeys? MakeGlobalKeys(IMainThread mainThread, Action<ShortcutAction> perform) => new KeyboardHook(mainThread, perform);

    public void ExcludeFromCapture(IntPtr window) => NativeWindow.SetExcludedFromCapture(window, excluded: true);

    public void SetClickThrough(IntPtr window, bool on) => NativeWindow.SetClickThrough(window, on);

    public void SetNeverActivates(IntPtr window, bool on) => NativeWindow.SetNeverActivates(window, on);

    public void HideFromTaskSwitcher(IntPtr window) => NativeWindow.SetHiddenFromTaskSwitcher(window, hidden: true);
}
