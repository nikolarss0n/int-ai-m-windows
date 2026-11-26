using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Presentation;

public sealed partial class InterviewController
{
    // MARK: - Screenshots

    public async Task CaptureScreenshotAsync()
    {
        if (screenCapture is null) return;
        Screenshot screenshot;
        try
        {
            screenshot = await screenCapture.CaptureScreenAsync();
        }
        catch (ScreenCaptureException error)
        {
            var detail = error.Failure == ScreenCaptureFailure.NoDisplayFound
                ? "No display found. Connect or wake a display, then try again."
                : FeedbackCopy.Sentence(error.Message) + " Try again.";
            mainThread.Post(() => PostNotice(FeedbackCopy.ScreenshotFailed(detail), () => _ = CaptureScreenshotAsync()));
            return;
        }
        await mainThread.RunAsync(() => AddScreenshot(screenshot));
    }

    public void AddScreenshot(Screenshot screenshot)
    {
        screenshots.Add(screenshot);
        voiceMessages.Add(new InterviewMessage(MessageType.Screenshot, "Screenshot", screenshotId: screenshot.Id));
        if (!shell.IsWindowActive) shell.ShowScreenshotAlert(screenshots);
        Refresh();
        var count = screenshots.Count;
        PostNotice(FeedbackNotice.Confirmation(
            "screenshot", count == 1 ? "Screenshot captured" : $"Screenshot {count} captured",
            detail: ScreenshotPresentation.LiveScreenshotStatusText(count)));
    }

    public async Task AnalyzeScreenshotsAsync()
    {
        if (screenshots.Count == 0)
        {
            PostNotice(FeedbackCopy.NothingToAnalyze);
            return;
        }
        var apiKey = ModelApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            await PromptForModelKeyAsync();
            return;
        }
        if (!SettingsStore.Standard.GetBool(DataConsentKey))
        {
            if (!await AskForDataConsentAsync()) return;
            SettingsStore.Standard.Set(DataConsentKey, true);
        }
        await PerformAnalysisAsync(new AnthropicClient(apiKey, LlmProvider));
    }

    private static (string Service, string Company, string? Policy) Recipient(InterviewLLMProvider provider) => provider switch
    {
        InterviewLLMProvider.Xai => ("xAI's Grok service", "xAI", null),
        InterviewLLMProvider.Anthropic => ("Anthropic's Claude service", "Anthropic", "https://www.anthropic.com/privacy"),
        _ => ("OpenAI's GPT service", "OpenAI", null),
    };

    private async Task<bool> AskForDataConsentAsync()
    {
        var (service, company, policy) = Recipient(LlmProvider);
        var message =
            $"To analyze your screenshots, Interview Master will send them to {service}.\n\n" +
            "What is shared:\n• Screenshot images you capture\n• Analysis prompts\n\n" +
            "What is NOT shared:\n• Your notes\n• Personal information\n• API keys\n\n" +
            $"{company} processes data according to its privacy policy. Screenshots are not stored permanently.\n\n" +
            $"Do you consent to share screenshot data with {company} for AI analysis?";
        var buttons = policy is null ? new[] { "I Consent", "Cancel" } : new[] { "I Consent", "Privacy Policy", "Cancel" };
        while (true)
        {
            var choice = await shell.AskAsync(new AppQuestion("Data sharing consent", message, buttons));
            if (policy is not null && choice == 1)
            {
                shell.OpenLink(policy);
                continue;
            }
            return choice == 0;
        }
    }

    /// Sends the screenshots and streams what comes back into the code panel.
    public async Task PerformAnalysisAsync(AnthropicClient client)
    {
        var images = screenshots.Select(screenshot => screenshot.ToBase64()).OfType<string>().ToList();
        var count = screenshots.Count;
        await mainThread.RunAsync(() =>
        {
            BeginCodeStream(new CodingTaskInfo(Guid.NewGuid(), "Screenshot analysis", IsRefinement: false, AllowsChanges: false));
            SetPinnedSolution($"🤔 Analyzing {count} screenshot{(count == 1 ? "" : "s")}…");
            if (PinnedSolution is { } pinned) PresentFloatingSolutionIfNeeded(pinned);
        });

        var response = new System.Text.StringBuilder();
        var outcome = await client.SendMessageStreamAsync(
            images, AnalysisMode.Prompt(),
            chunk =>
            {
                string snapshot;
                lock (response) snapshot = response.Append(chunk).ToString();
                mainThread.Post(() => SetPinnedSolution(snapshot));
            },
            AnalysisMode.Prefill());

        if (outcome.Error is { } error)
        {
            await mainThread.RunAsync(() =>
            {
                EndCodeStream(CodingTaskOutcome.Incomplete("The analysis failed."));
                SetPinnedSolution($"❌ Couldn't analyze the screenshots: {error.Message}");
            });
            await shell.AskAsync(AppQuestion.Notice(
                "Couldn't analyze the screenshots",
                FeedbackCopy.Sentence(error.Message) + $"\n\nCheck your connection and API key, then press {PlatformCopy.AnalyzeShortcut} to try again."));
            return;
        }
        string final;
        lock (response) final = response.ToString();
        await mainThread.RunAsync(() =>
        {
            SetPinnedSolution(final);
            EndCodeStream();
            PresentFloatingSolutionIfNeeded(final);
            screenshots.Clear();
            Refresh();
        });
    }

    /// Takes the screenshots and the pinned code away from the AI, with a way back.
    public void ClearScreenshots()
    {
        var clearedShots = screenshots.ToList();
        var clearedMessages = voiceMessages.Where(message => message.Type == MessageType.Screenshot).ToList();
        var clearedSolution = PinnedSolution;
        screenshots.Clear();
        voiceMessages.RemoveAll(message => message.Type == MessageType.Screenshot);
        PinnedSolution = null;
        codingTaskRecorded = false;
        Refresh();
        if (FeedbackCopy.Cleared(clearedShots.Count, clearedSolution is not null) is not { } summary)
        {
            PostNotice(FeedbackNotice.Info("cleared", "Nothing to clear"));
            return;
        }
        PostNotice(
            FeedbackNotice.Info("cleared", summary, detail: "The AI no longer sees them. Undo brings them back.", actionTitle: "Undo"),
            () =>
            {
                screenshots.InsertRange(0, clearedShots);
                voiceMessages.AddRange(clearedMessages);
                PinnedSolution = clearedSolution;
                codingTaskRecorded = clearedSolution is not null;
                Refresh();
                PostNotice(FeedbackNotice.Confirmation(
                    "cleared", FeedbackCopy.Restored(clearedShots.Count, clearedSolution is not null) ?? "Restored"));
            });
    }

    // MARK: - Export

    private static string DateForFileName() => DateTime.Now.ToString("yyyy-MM-dd_HHmm", System.Globalization.CultureInfo.InvariantCulture);

    private List<InterviewMessage> ExportableMessages(bool withBriefing) =>
        ExportInterviewUseCase.Messages(voiceMessages, interviewStartedAt)
            .Where(message => message.Type is MessageType.Question or MessageType.Answer or MessageType.FollowUp or MessageType.CodingTask
                || (withBriefing && message.Type == MessageType.Briefing))
            .ToList();

    public async Task ExportInterviewAsync()
    {
        var messages = ExportableMessages(withBriefing: true);
        if (messages.Count == 0)
        {
            PostNotice(FeedbackCopy.NothingToExport);
            return;
        }
        if (await shell.ChooseExportFileAsync($"interview_{DateForFileName()}.md") is not { } path) return;
        var format = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ExportFormat.Json : ExportFormat.Markdown;
        try
        {
            File.WriteAllText(path, new ExportInterviewUseCase().Execute(messages, format));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await shell.AskAsync(AppQuestion.Notice(
                "Couldn't export the interview",
                FeedbackCopy.Sentence(error.Message) + "\n\nChoose another location and try again.", critical: true));
            return;
        }
        shell.RevealFile(path);
        PostNotice(FeedbackCopy.Exported(Path.GetFileName(path), path));
    }

    /// Saves the interview that just ended. `Path` is null when there was nothing to save; `Error`
    /// says why a save failed.
    public (string? Path, string? Error) AutoSaveSession()
    {
        var messages = ExportableMessages(withBriefing: false);
        if (messages.Count == 0) return (null, null);
        var fileName = $"session_{DateForFileName()}.json";
        var path = Path.Combine(SessionsDirectory, fileName);
        try
        {
            Directory.CreateDirectory(SessionsDirectory);
            File.WriteAllText(path, new ExportInterviewUseCase().Execute(messages, ExportFormat.Json));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Print($"Could not save the session to {path}: {error.Message}");
            return (path, error.Message);
        }
        if (anthropicClient is { } client && AnalyzesSavedSessions) _ = new SessionAnalysisUseCase().Analyze(messages, fileName, client);
        return (path, null);
    }

    /// Where finished interviews are kept.
    public string SessionsDirectory { get; set; } = AppPaths.SessionsDirectory;

    /// Whether a saved session is sent off to be summarised into the interview memory.
    public bool AnalyzesSavedSessions { get; set; } = true;
}
