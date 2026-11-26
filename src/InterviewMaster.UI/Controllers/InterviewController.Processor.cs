using Avalonia.Threading;
using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;

namespace InterviewMaster.Presentation;

public sealed partial class InterviewController
{
    // MARK: - What the processor reports

    public void ProcessorDidUpdateBriefing(InterviewBriefing briefing)
    {
        if (briefing.TalkingPoints.Count == 0)
        {
            Focus.Briefing.Update(null);
        }
        else
        {
            UpsertBriefingMessage(briefing);
            Focus.Briefing.Update(briefing);
        }
        Focus.RefreshBody();
    }

    public void ProcessorDidUpdateCodingTask(string content)
    {
        if (PinnedSolution is null) DebugLog($"processorDidUpdateCodingTask: {content.Length} chars, code panel hidden={!Focus.CodePanel.IsVisible}");
        SetPinnedSolution(content);
        PresentFloatingSolutionIfNeeded(content);
    }

    public void ProcessorDidStartCodingTask(CodingTaskInfo info)
    {
        DebugLog($"processorDidStartCodingTask: refinement={info.IsRefinement}");
        BeginCodeStream(info);
    }

    public void ProcessorDidFinishCodingTask(CodingTaskOutcome outcome)
    {
        DebugLog($"processorDidFinishCodingTask: {Focus.CodePanel.Steps.Count} step(s) on the panel, complete={outcome.IsComplete}");
        EndCodeStream(outcome);
    }

    public void ProcessorShowLoading(string message, LoadingColor color, Guid turnId) => ShowLoading(message, turnId);

    public void ProcessorHideLoading(Guid turnId) => HideLoading(turnId);

    public void ProcessorDidReceiveQuestion(string text, string topic, MessageType messageType, CaptureDevice source, Guid turnId, int sequence)
    {
        DebugLog($"processorDidReceiveQuestion: {text.Length} chars, topic={topic}");
        ReceiveQuestion(turnId, sequence, text, topic);
        ResolveNotice("pipeline");
        AddMessage(MessageType.Question, text, topic, source, turnId, sequence);
    }

    public void ProcessorDidStartStreaming(MessageType messageType, string topic, int? latencyMs, Guid turnId, int sequence)
    {
        DebugLog($"processorDidStartStreaming: type={messageType} topic={topic} cardStart={latencyMs ?? -1}ms");
        ResetStreamingThrottle(turnId);
        StartAnswer(turnId);
        // The reply stands as a caret until its first words arrive.
        voiceMessages.Add(new InterviewMessage(messageType, "▌", topic, turnId: turnId, turnSequence: sequence));
    }

    public void ProcessorDidReceiveAnswerChunk(string fullContent, Guid turnId)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            mainThread.Post(() => ProcessorDidReceiveAnswerChunk(fullContent, turnId));
            return;
        }
        if (fullContent.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
        {
            ResetStreamingThrottle(turnId);
            FailTurn(turnId, FeedbackCopy.FailureReason(fullContent));
            FinalizeStreamingMessage(fullContent, null, turnId);
            return;
        }
        pendingStreamingContent[turnId] = fullContent;
        if (ShouldFlushStreamingContent(fullContent, turnId))
        {
            FlushPendingStreamingContent(turnId);
            return;
        }
        if (streamingFlushes.ContainsKey(turnId)) return;
        streamingFlushes[turnId] = Delayed.After(StreamingUpdateInterval, () => FlushPendingStreamingContent(turnId));
    }

    public void ProcessorDidFinishAnswer(string fullAnswer, int? totalLatencyMs, Guid turnId)
    {
        DebugLog($"processorDidFinishAnswer: {fullAnswer.Length} chars, total={totalLatencyMs ?? -1}ms");
        ResetStreamingThrottle(turnId);
        FinishAnswer(turnId, fullAnswer, totalLatencyMs);
        FinalizeStreamingMessage(fullAnswer, totalLatencyMs, turnId);
    }

    public void ProcessorDidUpdateStatus(string message)
    {
        if (message.StartsWith("error:", StringComparison.OrdinalIgnoreCase)) AddMessage(MessageType.Status, message, "error");
        if (FeedbackCopy.NoticeForStatus(message) is not { } notice) return;
        PostNotice(notice, notice.ActionTitle == FeedbackCopy.OpenSettings ? shell.ShowSettings : null);
    }

    // MARK: - Streaming, a few times a second at most

    private bool ShouldFlushStreamingContent(string content, Guid turnId)
    {
        if (content.Length < 80 || content.EndsWith('\n')) return true;
        var last = lastStreamingUpdates.TryGetValue(turnId, out var at) ? at : DateTime.MinValue;
        return (Now() - last).TotalSeconds >= StreamingUpdateInterval;
    }

    private void FlushPendingStreamingContent(Guid turnId)
    {
        if (streamingFlushes.Remove(turnId, out var flush)) flush.Dispose();
        if (!pendingStreamingContent.Remove(turnId, out var content)) return;
        lastStreamingUpdates[turnId] = Now();
        UpdateAnswer(turnId, content);
    }

    private void ResetStreamingThrottle(Guid? turnId = null)
    {
        if (turnId is { } id)
        {
            if (streamingFlushes.Remove(id, out var flush)) flush.Dispose();
            pendingStreamingContent.Remove(id);
            lastStreamingUpdates[id] = DateTime.MinValue;
            return;
        }
        foreach (var flush in streamingFlushes.Values) flush.Dispose();
        streamingFlushes.Clear();
        pendingStreamingContent.Clear();
        lastStreamingUpdates.Clear();
    }

    // MARK: - The record of the interview

    public void AddMessage(MessageType type, string content, string? topic, CaptureDevice? audioSource = null, Guid? turnId = null, int? turnSequence = null)
    {
        voiceMessages.Add(new InterviewMessage(type, content, topic, audioSource: audioSource, turnId: turnId, turnSequence: turnSequence));
        if (type is MessageType.Question or MessageType.Answer or MessageType.FollowUp) UpdateFloatingQA();
    }

    private void UpsertBriefingMessage(InterviewBriefing briefing)
    {
        var message = new InterviewMessage(MessageType.Briefing, Briefing.FormatBriefingContent(briefing), briefing.Thread);
        var index = voiceMessages.FindIndex(existing => existing.Type == MessageType.Briefing);
        if (index >= 0) voiceMessages[index] = message;
        else voiceMessages.Add(message);
    }

    private void FinalizeStreamingMessage(string content, int? totalLatencyMs, Guid turnId)
    {
        var index = voiceMessages.FindLastIndex(message => message.TurnId == turnId && message.IsAnswer);
        if (index < 0) return;
        var previous = voiceMessages[index];
        voiceMessages[index] = new InterviewMessage(
            previous.Type, content, previous.Topic, audioSource: previous.AudioSource,
            responseLatencyMs: totalLatencyMs ?? previous.ResponseLatencyMs, turnId: turnId, turnSequence: previous.TurnSequence);
        UpdateFloatingQA();
    }

    private void UpdateFloatingQA()
    {
        if (shell.FloatingSolution is { IsOpen: true } floating) floating.UpdateQA();
    }

    // MARK: - Starting and ending the interview

    public void ToggleInterview()
    {
        if (IsInterviewActive) StopInterview();
        else _ = StartInterviewAsync();
    }

    public async Task StartInterviewAsync()
    {
        var groqKey = ApiKeyManager.Shared.GetKey(ApiKeyType.Groq);
        if (string.IsNullOrEmpty(groqKey))
        {
            await PromptForKeyAsync(
                "Groq API key needed",
                "Interview Master uses Groq to transcribe the interviewer's voice.\n\nAdd your key in Settings. You can get one at console.groq.com.");
            return;
        }
        var modelKey = ModelApiKey;
        if (string.IsNullOrEmpty(modelKey))
        {
            await PromptForModelKeyAsync();
            return;
        }
        groqClient = new GroqInterviewClient(groqKey);
        anthropicClient = new AnthropicClient(modelKey, LlmProvider) { JevDecisionClient = JevDecisionClient.Configured() };
        Processor.Configure(groqClient, anthropicClient);
        BeginInterview();
        BeginAudioSession();
    }

    /// The answer model was changed in the menu: the next question uses it without restarting the interview.
    public void AnswerModelChanged()
    {
        if (!IsInterviewActive || groqClient is null || ModelApiKey is not { Length: > 0 } key) return;
        anthropicClient = new AnthropicClient(key, LlmProvider) { JevDecisionClient = JevDecisionClient.Configured() };
        Processor.Configure(groqClient, anthropicClient);
    }

    /// Everything a start does on screen, without the clients or the audio.
    public void BeginInterview()
    {
        IsInterviewActive = true;
        interviewStartedAt = DateTime.Now;
        ResetFocus();
        screenshots.Clear();
        ShowRecordingIndicator();
        AddMessage(MessageType.Status, "Interview started. Listening for questions…", null);
    }

    public void StopInterview()
    {
        IsInterviewActive = false;
        EndAudioSession();
        Processor.Reset();
        ConversationContext.Clear();
        Focus.CodePanel.LockChanges();
        HideLoading();
        ResetStreamingThrottle();
        HideRecordingIndicator();
        AddMessage(MessageType.Status, "Interview ended", null);
        var saved = AutoSaveSession();
        if (saved.Path is { } path)
        {
            if (saved.Error is null) PostNotice(FeedbackCopy.SessionSaved(path), () => shell.RevealFile(path));
            else PostNotice(FeedbackCopy.SessionNotSaved(saved.Error), () => _ = ExportInterviewAsync());
        }
        Refresh();
    }

    private Task PromptForModelKeyAsync() => PromptForKeyAsync(
        "API key needed for answers",
        $"Answers and screenshot analysis need an xAI Grok key or an Anthropic key.\n\nAdd XAI_API_KEY to {PlatformCopy.KeysFileDisplayPath} (get one at console.x.ai) and restart the app, or enter an Anthropic API key in Settings.");

    private async Task PromptForKeyAsync(string title, string message)
    {
        if (await shell.AskAsync(new AppQuestion(title, message, new[] { "Open Settings", "Cancel" })) == 0) shell.ShowSettings();
    }

    private void ShowRecordingIndicator()
    {
        RecordingStartTime = Now();
        recordingTimer?.Stop();
        recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        recordingTimer.Tick += (_, _) => RefreshActivity();
        recordingTimer.Start();
        Refresh();
    }

    private void HideRecordingIndicator()
    {
        recordingTimer?.Stop();
        recordingTimer = null;
        RecordingStartTime = null;
    }

    /// Stops the timers. The window is closing.
    public void Shutdown()
    {
        recordingTimer?.Stop();
        transcriptionRefresh?.Dispose();
        readyReset?.Dispose();
        noticeExpiry?.Dispose();
        pushToTalkWatchdog?.Dispose();
        ResetStreamingThrottle();
        Audio.Terminate();
    }
}
