using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;

namespace InterviewMaster.Presentation;

/// Opens the Settings window and writes what it holds back when Save is pressed. A port of
/// `SettingsWindowController.swift`.
public sealed class SettingsController
{
    private readonly IAppShell shell;
    private readonly Action settingsChanged;
    private IPanel? panel;

    /// `settingsChanged` runs after a save, so the main window shows the new role and languages.
    public SettingsController(IAppShell shell, Action settingsChanged)
    {
        this.shell = shell;
        this.settingsChanged = settingsChanged;
    }

    public SettingsView? View { get; private set; }

    public void Show()
    {
        panel?.Close();
        var view = new SettingsView();
        var settings = AppSettings.Shared;
        SettingsView.Fill(view.Role, InterviewRoles.SelectableCases.Select(role => role.DisplayName()), settings.Role.DisplayName());
        SettingsView.Fill(view.Language, ProgrammingLanguages.AllCases.Select(language => language.DisplayName()), settings.ProgrammingLanguage.DisplayName());
        SettingsView.Fill(view.Listening, SpeakingLanguages.AllCases.Select(language => language.DisplayName()), settings.ListeningLanguage.DisplayName());
        SettingsView.Fill(view.Response, SpeakingLanguages.AllCases.Select(language => language.DisplayName()), settings.ResponseLanguage.DisplayName());
        view.Frameworks.Text = settings.Frameworks;
        view.ResumeStatus = ResumeStore.Shared.HasResume ? ResumeStore.Shared.FileName : "Not attached";
        view.AnthropicKey.Text = ApiKeyManager.Shared.GetKey(ApiKeyType.Anthropic) ?? "";
        view.GroqKey.Text = ApiKeyManager.Shared.GetKey(ApiKeyType.Groq) ?? "";

        view.Attach.Click += () => _ = AttachResumeAsync(view);
        view.Clear.Click += () =>
        {
            ResumeStore.Shared.Clear();
            view.ResumeStatus = "Not attached";
        };
        view.Save.Click += () => Save(view);
        view.Cancel.Click += Close;
        View = view;
        panel = shell.ShowPanel("Settings", view, SettingsView.PanelWidth, SettingsView.PanelHeight);
    }

    public void Save(SettingsView view)
    {
        var settings = AppSettings.Shared;
        if (view.Role.SelectedIndex >= 0) settings.Role = InterviewRoles.SelectableCases[view.Role.SelectedIndex];
        if (view.Language.SelectedIndex >= 0) settings.ProgrammingLanguage = ProgrammingLanguages.AllCases[view.Language.SelectedIndex];
        if (view.Listening.SelectedIndex >= 0) settings.ListeningLanguage = SpeakingLanguages.AllCases[view.Listening.SelectedIndex];
        if (view.Response.SelectedIndex >= 0) settings.ResponseLanguage = SpeakingLanguages.AllCases[view.Response.SelectedIndex];
        settings.Frameworks = (view.Frameworks.Text ?? "").Trim();

        var anthropicKey = (view.AnthropicKey.Text ?? "").Trim();
        var groqKey = (view.GroqKey.Text ?? "").Trim();
        if (anthropicKey.Length > 0) ApiKeyManager.Shared.SetKey(anthropicKey, ApiKeyType.Anthropic);
        if (groqKey.Length > 0) ApiKeyManager.Shared.SetKey(groqKey, ApiKeyType.Groq);

        ApiKeyManager.NotifyKeysUpdated();
        settingsChanged();
        Close();
    }

    public void Close()
    {
        panel?.Close();
        panel = null;
        View = null;
    }

    /// Formats the CV can be read from. A PDF needs a text extractor, which this build may not have.
    public static IReadOnlyList<string> ResumeExtensions =>
        ResumeStore.Shared.PdfTextExtractor is null ? new[] { "md", "markdown", "txt" } : new[] { "pdf", "md", "markdown", "txt" };

    private async Task AttachResumeAsync(SettingsView view)
    {
        if (await shell.ChooseOpenFileAsync("Attach CV", ResumeExtensions) is not { } path) return;
        try
        {
            ResumeStore.Shared.Attach(path);
            view.ResumeStatus = ResumeStore.Shared.FileName;
        }
        catch (Exception error) when (error is ResumeStoreException or IOException or UnauthorizedAccessException)
        {
            await shell.AskAsync(AppQuestion.Notice("Couldn't read the CV", error.Message, critical: true));
        }
    }
}
