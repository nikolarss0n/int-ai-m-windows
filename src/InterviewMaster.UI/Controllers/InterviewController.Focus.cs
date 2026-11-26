using InterviewMaster.Domain;

namespace InterviewMaster.Presentation;

public sealed partial class InterviewController
{
    // MARK: - Waiting

    public void ShowLoading(string text, Guid turnId) => mainThread.Post(() =>
    {
        var normalized = text.ToLowerInvariant();
        if (normalized.Contains("transcrib")) BeginTranscription(turnId);
        if (normalized.Contains("analyz") || normalized.Contains("generating") || normalized.Contains("writing code")) BeginAIWork(turnId);
        if (shell.FloatingSolution is { IsOpen: true } floating) floating.ShowLoading();
    });

    public void HideLoading(Guid turnId) => mainThread.Post(() =>
    {
        EndTranscription(turnId);
        EndAIWork(turnId);
        if (Questions.IsAIWorking) shell.FloatingSolution?.ShowLoading();
        else shell.FloatingSolution?.HideLoading();
    });

    public void HideLoading() => mainThread.Post(() =>
    {
        ResetTranscription();
        EndAllAIWork();
        shell.FloatingSolution?.HideLoading();
    });

    public void BeginTranscription(Guid turnId)
    {
        transcription.Begin(turnId, Now());
        ScheduleTranscriptionRefresh();
    }

    public void EndTranscription(Guid turnId)
    {
        if (transcription.IsEmpty) return;
        transcription.End(turnId);
        ScheduleTranscriptionRefresh();
        RefreshActivity();
    }

    public void ResetTranscription()
    {
        transcription.RemoveAll();
        transcriptionRefresh?.Dispose();
        transcriptionRefresh = null;
    }

    /// The indicator appears after a moment and gives up after a while, so it is looked at again
    /// when either is due.
    private void ScheduleTranscriptionRefresh()
    {
        transcriptionRefresh?.Dispose();
        transcriptionRefresh = null;
        if (transcription.NextChange(Now()) is not { } next) return;
        transcriptionRefresh = Delayed.After((next - Now()).TotalSeconds, () =>
        {
            RefreshActivity();
            ScheduleTranscriptionRefresh();
        });
    }

    public void BeginAIWork(Guid turnId)
    {
        EndTranscription(turnId);
        Questions.BeginAIWork(turnId);
        answerReadyVisible = false;
        readyReset?.Dispose();
        Refresh();
    }

    public void EndAIWork(Guid turnId)
    {
        Questions.EndAIWork(turnId);
        Refresh();
    }

    public void EndAllAIWork()
    {
        Questions.EndAllAIWork();
        Refresh();
    }

    // MARK: - Questions and answers

    public void ReceiveQuestion(Guid turnId, int sequence, string text, string topic)
    {
        Questions.ReceiveQuestion(turnId, sequence, text, topic, Now());
        Refresh();
        if (CodeOpen && topic != "coding") ShowAnswerOverCode(true);
    }

    public void StartAnswer(Guid turnId)
    {
        Questions.StartStreaming(turnId);
        Refresh();
    }

    public void UpdateAnswer(Guid turnId, string content)
    {
        Questions.UpdateAnswer(turnId, content);
        Refresh();
    }

    public void FinishAnswer(Guid turnId, string content, int? latencyMs)
    {
        Questions.FinishAnswer(turnId, content, latencyMs);
        answerReadyVisible = true;
        readyReset?.Dispose();
        readyReset = Delayed.After(Token.Motion.ReadyHold, () =>
        {
            answerReadyVisible = false;
            Refresh();
        });
        Refresh();
    }

    public void FailTurn(Guid turnId, string? reason = null)
    {
        Questions.Fail(turnId, reason);
        Refresh();
    }

    public void RetryAnswer(Guid turnId)
    {
        if (!IsInterviewActive) return;
        var entry = Questions.Entries.FirstOrDefault(candidate => candidate.Id == turnId);
        if (entry is null || entry.Phase != QuestionBurstPhase.Failed) return;
        Questions.Restart(turnId, Now());
        Refresh();
        Processor.RetryAnswer(turnId, entry.Question, entry.Topic, entry.Sequence);
    }

    public void SelectQuestion(Guid id)
    {
        Questions.Select(id);
        Focus.CodePanel.SelectVersion(id);
        Refresh();
    }

    public void MoveQuestionSelection(bool newer)
    {
        if (Questions.NeighbourOfSelection(newer) is { } id) SelectQuestion(id);
    }

    /// Back to the empty live view: no questions, no CV strip, no code.
    public void ResetFocus()
    {
        ResetTranscription();
        readyReset?.Dispose();
        readyReset = null;
        answerReadyVisible = false;
        Questions.Clear();
        Focus.Briefing.Update(null);
        ResetCodePanel();
        Refresh();
    }

    public void ClearTimeline()
    {
        voiceMessages.Clear();
        ResetFocus();
    }

    // MARK: - Notices

    public void PostNotice(FeedbackNotice notice, Action? action = null)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            mainThread.Post(() => PostNotice(notice, action));
            return;
        }
        if (!notices.Show(notice, Now())) return;
        noticeActions[notice.Key] = action;
        RenderNotice();
        shell.Announce(string.Join(". ", new[] { notice.Message, notice.Detail }.Where(part => part is not null)), notice.Kind == FeedbackKind.Error);
    }

    public void ResolveNotice(string key)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            mainThread.Post(() => ResolveNotice(key));
            return;
        }
        if (notices.Current?.Key != key) return;
        notices.Resolve(key);
        noticeActions.Remove(key);
        RenderNotice();
    }

    public void DismissNotice()
    {
        if (notices.Current?.Key is { } key) noticeActions.Remove(key);
        notices.Dismiss();
        RenderNotice();
    }

    public void RunNoticeAction()
    {
        if (notices.Current?.Key is not { } key) return;
        noticeActions.TryGetValue(key, out var action);
        DismissNotice();
        action?.Invoke();
    }

    private void RenderNotice()
    {
        noticeExpiry?.Dispose();
        noticeExpiry = null;
        notices.Expire(Now());
        var notice = notices.Current;
        Focus.Header.Notice.Show(notice);
        Focus.Header.SetTitleHidden(notice is not null);
        if (notices.Expiry is not { } expiry) return;
        noticeExpiry = Delayed.After((expiry - Now()).TotalSeconds, RenderNotice);
    }

    // MARK: - The code panel

    /// The code names the question that asked for it, by number and words. A click goes to that question.
    private void RefreshCodeOrigin()
    {
        var panel = Focus.CodePanel;
        if (panel.Displayed is not { } version || Questions.QuestionNumber(version.Id) is not { } number)
        {
            panel.SetOrigin(null);
            return;
        }
        var request = version.Title.Trim();
        panel.SetOrigin(request.Length == 0 ? $"Q{number}" : $"Q{number}{Token.Text.Separator}{request}");
    }

    public void CopyCode()
    {
        if (CodeOpen) Focus.CodePanel.CopyDisplayedCode();
    }

    private void ShowCodeInPanel(string content)
    {
        if (!shell.IsWindowVisible) return;
        Focus.CodePanel.UpdateLatest(content);
        if (!CodeOpen && !codePanelDismissed) OpenCodePanel();
        UpdateCodeButton();
    }

    public void BeginCodeStream(CodingTaskInfo info)
    {
        if (!shell.IsWindowVisible) return;
        if (!info.IsRefinement) codePanelDismissed = false;
        Focus.CodePanel.BeginVersion(info);
        Refresh();
        if (!CodeOpen && !codePanelDismissed) OpenCodePanel();
        if (!info.IsRefinement) ShowAnswerOverCode(false);
        UpdateCodeButton();
    }

    public void EndCodeStream(CodingTaskOutcome? outcome = null)
    {
        Focus.CodePanel.FinishLatest(outcome ?? CodingTaskOutcome.Complete);
        UpdateCodeButton();
    }

    private void OpenCodePanel()
    {
        DebugLog("code panel opened");
        Focus.CodeOpen = true;
        ShowAnswerOverCode(false);
    }

    public void HideCodePanel(bool byUser)
    {
        if (byUser) codePanelDismissed = true;
        if (!CodeOpen) return;
        DebugLog($"code panel hidden (byUser={byUser})");
        Focus.CodeOpen = false;
        ShowAnswerOverCode(false);
    }

    /// One key walks the three states: code, the full answer over it, closed.
    public void ToggleCodePanel()
    {
        if (!CodeOpen)
        {
            codePanelDismissed = false;
            OpenCodePanel();
        }
        else if (AnswerOverCode)
        {
            ShowAnswerOverCode(false);
        }
        else
        {
            HideCodePanel(byUser: true);
        }
        UpdateCodeButton();
    }

    public void ShowAnswerOverCode(bool open)
    {
        Focus.AnswerOverCode = open && CodeOpen;
        RefreshTalkStrip();
        Focus.RefreshBody();
        UpdateCodeButton();
    }

    public void ToggleAnswerOverCode() => ShowAnswerOverCode(!AnswerOverCode);

    private void RefreshTalkStrip()
    {
        var selected = Questions.SelectedEntry;
        var line = selected is null ? "" : CodingSteps.FirstTalkingPoint(selected.Answer) ?? selected.Question;
        Focus.TalkStrip.Update(line, AnswerOverCode);
    }

    /// The window came back: code that arrived while it was hidden goes into the panel.
    public void SyncCodePanelWithPinnedSolution()
    {
        if (Focus.CodePanel.Versions.Count == 0 && PinnedSolution is { } solution) ShowCodeInPanel(solution);
    }

    private void ResetCodePanel()
    {
        codePanelDismissed = false;
        Focus.CodePanel.Clear();
        if (CodeOpen)
        {
            Focus.CodeOpen = false;
            ShowAnswerOverCode(false);
        }
        PinnedSolution = null;
        codingTaskRecorded = false;
        UpdateCodeButton();
    }

    /// The latest code, in the panel and in the record of the interview.
    private void SetPinnedSolution(string solution)
    {
        PinnedSolution = solution;
        ShowCodeInPanel(solution);
        var record = new InterviewMessage(MessageType.CodingTask, solution, "solution");
        var index = voiceMessages.FindLastIndex(message => message.Type == MessageType.CodingTask);
        if (index >= 0 && codingTaskRecorded) voiceMessages[index] = record;
        else voiceMessages.Add(record);
        codingTaskRecorded = true;
        if (shell.FloatingSolution is { IsOpen: true } floating) floating.UpdateContent(solution);
    }

    /// Code lives in the docked panel while the main window is up. With the window hidden it appears
    /// in the floating overlay instead.
    private void PresentFloatingSolutionIfNeeded(string content)
    {
        if (shell.IsWindowVisible || shell.FloatingSolution is not { } floating) return;
        if (!ScreenshotPresentation.ShouldPresentScreenshotAnalysisOverlay(shell.IsWindowVisible)) return;
        if (!floating.IsOpen) floating.Show();
        floating.UpdateContent(content);
    }
}
