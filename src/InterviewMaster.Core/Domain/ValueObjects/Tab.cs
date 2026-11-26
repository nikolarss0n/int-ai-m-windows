namespace InterviewMaster.Domain;

/// Value Object: Tab
/// Represents the different tabs/views in the application
public enum Tab
{
    Notes,
    Coding,
    Voice,
    Practice
}

public static class Tabs
{
    public static string Title(this Tab tab) => tab switch
    {
        Tab.Notes => "📝 Interview Notes",
        Tab.Coding => "💻 Coding Task",
        Tab.Voice => "🎤 Voice Assistant",
        _ => "Practice"
    };

    public static string KeyboardShortcut(this Tab tab) => tab switch
    {
        Tab.Notes => "Ctrl+1",
        Tab.Coding => "Ctrl+2",
        Tab.Voice => "Ctrl+3",
        _ => "Ctrl+4"
    };
}
