namespace InterviewMaster.Domain;

/// The words that differ between the macOS and the Windows app: shortcut names, the file manager and
/// where the keys file lives. The macOS sources write these inline (⌘S, Finder, ~/...).
public static class PlatformCopy
{
    public const string CaptureShortcut = "Ctrl+S";
    public const string AnalyzeShortcut = "Ctrl+Enter";
    public const string ClickThroughShortcut = "Ctrl+I";
    public const string MuteShortcut = "Ctrl+Shift+M";
    public const string AnswerOverCodeShortcut = "Ctrl+Alt+A";
    public const string ShowInFileManager = "Show in Explorer";
    public const string KeysFileDisplayPath = @"%USERPROFILE%\.interview-master-keys";
    public const string MicrophonePermissionHelp =
        "Microphone access is off. Turn on \"Let desktop apps access your microphone\" in Settings → Privacy & security → Microphone.";
    public const string MicrophoneSettingsLink = "ms-settings:privacy-microphone";
    public const string MicrophoneDisclosure =
        "Windows shows its microphone indicator while this is on. Press Ctrl+Shift+M to mute while you speak. Microphone sensitivity is a first estimate until it is calibrated for an interviewer across the table.";
    public const string ScreenshotAlertShortcuts = "Ctrl+B Show App  •  Ctrl+Enter Analyze  •  Ctrl+G Clear";
    public const string FloatingSolutionShortcuts = @"Ctrl+B Show  •  Ctrl+\ Hide  •  Ctrl+J/K Scroll  •  Ctrl+Arrows Move";
    public const string KeysSettingsHelp =
        @"Keys entered here apply until the app closes. To keep them, add them to %USERPROFILE%\.interview-master-keys.";
    public const string OpenAIKeyNeeded =
        @"Add OPENAI_API_KEY to %USERPROFILE%\.interview-master-keys, then restart the app to use this model.";
    public const string KeyboardShortcutsHelp = """
        Global (work from any app):
        Ctrl+B    Toggle window visibility
        Ctrl+S    Capture screenshot
        Ctrl+Enter    Analyze screenshots
        Ctrl+I    Toggle ghost mode

        Navigation:
        Ctrl+1    Context tab
        Ctrl+2    Timeline tab
        Ctrl+3    Show or hide the code panel
        Ctrl+4    Practice tab

        Editing:
        Ctrl+F    Find in notes
        Ctrl+G    Clear screenshots
        Ctrl+,    Settings

        Interview:
        Ctrl+Shift+M    Mute interview audio: hold, or tap to latch

        Window:
        Ctrl+Alt+Shift+Arrows    Move window, from any app
        """;
    public const string SystemAudioUnavailableHelp =
        "System audio could not be captured. Check that a playback device is connected and enabled in Settings → System → Sound.";
}
