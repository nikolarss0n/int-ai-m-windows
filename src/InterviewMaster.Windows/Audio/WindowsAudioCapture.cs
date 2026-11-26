using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;
using Microsoft.Win32;

namespace InterviewMaster.Platform.Windows.Audio;

/// The Windows counterpart of `AudioCaptureFactory`: permission checks and the capture objects the
/// coordinator starts.
public static class WindowsAudioCapture
{
    private const string MicrophoneConsent = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    /// Windows asks nothing to capture what the computer plays. The microphone has two privacy
    /// switches, "Microphone access" and "Let desktop apps access your microphone"; either one off
    /// blocks a desktop app.
    public static bool HasPermission(CaptureDevice device)
    {
        if (device == CaptureDevice.SystemAudio) return true;
        return !IsDenied(Registry.LocalMachine, MicrophoneConsent)
            && !IsDenied(Registry.CurrentUser, MicrophoneConsent)
            && !IsDenied(Registry.CurrentUser, MicrophoneConsent + @"\NonPackaged");
    }

    private static bool IsDenied(RegistryKey root, string path)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue("Value") is string value && value.Equals("Deny", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// Windows has no permission prompt for desktop apps, so `allowPrompt` changes nothing here; the
    /// caller opens the settings page instead.
    public static Task PreflightAsync(CaptureDevice device, bool allowPrompt = true) =>
        HasPermission(device) ? Task.CompletedTask : Task.FromException(new AudioCapturePermissionException(device));

    public static IAudioSourceCapture Make(CaptureDevice device, IMainThread mainThread) =>
        new PcmCapture(device, mainThread, () => new WasapiSource(device), PreflightAsync);

    /// Records a spoken practice answer from the default microphone.
    public static IPracticeRecorder MakePracticeRecorder() => new PcmPracticeRecorder(
        () => new WasapiSource(CaptureDevice.Microphone), () => PreflightAsync(CaptureDevice.Microphone));

    public const string MicrophoneSettingsUri = "ms-settings:privacy-microphone";
}
