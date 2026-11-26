namespace InterviewMaster.Domain;

/// Value Object: Analysis Mode
/// Smart analysis that adapts to screenshot content
public enum AnalysisMode
{
    Smart
}

public static class AnalysisModes
{
    /// Prefill to force model to start solution immediately (prevents preamble)
    /// Empty prefill since task type varies - prompt handles format
    public static string Prefill(this AnalysisMode mode) => "";

    public static string Prompt(this AnalysisMode mode)
    {
        var settings = AppSettings.Shared;
        return CodingSteps.Prompt(
            settings.ProgrammingLanguage.CodeBlockLang(),
            "Read the screenshot(s) and implement the asked coding task as typed steps.")
            + $"\n{settings.LanguageInstruction}";
    }
}
