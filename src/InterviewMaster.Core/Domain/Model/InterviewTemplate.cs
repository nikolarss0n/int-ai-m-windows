using InterviewMaster.Support;

namespace InterviewMaster.Domain;

public enum TemplateCategory
{
    Behavioral,
    SystemDesign,
    TestAutomation,
    Coding,
    LanguageSpecific
}

public static class TemplateCategories
{
    public static readonly IReadOnlyList<TemplateCategory> AllCases = new[]
    {
        TemplateCategory.Behavioral, TemplateCategory.SystemDesign, TemplateCategory.TestAutomation,
        TemplateCategory.Coding, TemplateCategory.LanguageSpecific
    };

    public static string RawValue(this TemplateCategory category) => category switch
    {
        TemplateCategory.Behavioral => "behavioral",
        TemplateCategory.SystemDesign => "systemDesign",
        TemplateCategory.TestAutomation => "testAutomation",
        TemplateCategory.Coding => "coding",
        _ => "languageSpecific"
    };

    public static string DisplayName(this TemplateCategory category) => category switch
    {
        TemplateCategory.Behavioral => "Behavioral",
        TemplateCategory.SystemDesign => "System Design",
        TemplateCategory.TestAutomation => "QA/SDET",
        TemplateCategory.Coding => "Coding",
        _ => "Language-Specific"
    };
}

public enum TemplateDifficulty
{
    Easy,
    Medium,
    Hard
}

public static class TemplateDifficulties
{
    public static string RawValue(this TemplateDifficulty difficulty) => difficulty switch
    {
        TemplateDifficulty.Easy => "easy",
        TemplateDifficulty.Medium => "medium",
        _ => "hard"
    };
}

public sealed record InterviewTemplate(string Id, string Name, string Description, TemplateCategory Category, ValueList<TemplateQuestion> Questions);

public sealed record TemplateQuestion(string Text, string Topic, TemplateDifficulty Difficulty, ValueList<string> Hints);
