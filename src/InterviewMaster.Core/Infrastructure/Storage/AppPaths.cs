namespace InterviewMaster.Infrastructure;

/// Where the app keeps its files. The folder names are the macOS ones, under the Windows user profile.
public static class AppPaths
{
    public static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// Documents\InterviewMaster: memory.json, sessions and practice history.
    public static string DocumentsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "InterviewMaster");

    public static string MemoryFile => Path.Combine(DocumentsRoot, "memory.json");

    public static string SessionsDirectory => Path.Combine(DocumentsRoot, "sessions");

    public static string PracticeDirectory => Path.Combine(DocumentsRoot, "practice");

    /// %USERPROFILE%\.interview-master-keys, the same dotfile the macOS app reads from the home folder.
    public static string KeysFile => Path.Combine(UserProfile, ".interview-master-keys");

    /// %LOCALAPPDATA%\InterviewMaster\logs: the debug log and the app log. They can hold what was said
    /// in an interview, so they stay in the user's own profile.
    public static string LogsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InterviewMaster", "logs");

    /// %APPDATA%\InterviewMaster\settings.json, standing in for the macOS preferences domain.
    public static string SettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InterviewMaster", "settings.json");
}
