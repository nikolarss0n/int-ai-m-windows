namespace InterviewMaster.AppServices;

/// The keyboard as the app hears it from any other app. Windows supplies a system-wide hook; without
/// one the app has only the keys its own window receives.
public interface IGlobalKeys : IDisposable
{
    /// Published by the UI thread whenever the window, tab, interview or dialogs change.
    ShortcutState State { get; set; }

    /// Told, on the UI thread, about each of the app's keys going down while `ObservesKeys` is on.
    Action<KeyStroke>? KeyObserved { get; set; }

    bool ObservesKeys { get; set; }

    /// Throws when the system refuses.
    void Install();

    /// Forgets a held mute key when a session ends.
    void ResetPushToTalk();
}
