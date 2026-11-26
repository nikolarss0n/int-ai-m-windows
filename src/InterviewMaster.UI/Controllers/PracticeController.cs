using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Presentation;

/// Runs the practice tab: what to practise, the session, its autosave, and the saved runs. A port of
/// `PracticeTabController.swift`; its modal alerts are questions asked through the shell here.
public sealed partial class PracticeController
{
    private static readonly (string Title, int Count)[] CountChoices = { ("5", 5), ("10", 10), ("20", 20), ("50", 50), ("100", 100), ("120", 120) };

    private readonly PracticeStore store;
    private readonly IAppShell shell;
    private readonly IPracticeRecorder? recorder;
    private readonly Random random;
    private readonly PracticeBankFile bank;
    private readonly TimeSpan targetTimeOfDay = DateTime.Now.TimeOfDay;

    private string selectedRoleID = PracticeRole.AIEngineer.Id;
    private HashSet<string> selectedGroupIDs = new(PracticeRole.AIEngineer.GroupIds, StringComparer.Ordinal);
    private int selectedCount = 10;
    private PracticeRunMode selectedMode = PracticeRunMode.Learn;
    private List<PracticeRunMode> modeChoices = new();
    private List<int> countChoices = new();
    private bool fillingControls;

    /// Null while the answer is being written; the review once the key ideas are shown.
    private PracticeRecallReview? reviewing;
    private List<string> recallDrafts = new();
    private List<bool> recallUsedHelp = new();
    private List<PracticeRecallResponse?> recallResponses = new();
    private List<PracticeRecallConfidence?> recallConfidences = new();
    private List<List<PracticeCoverageOverride>> recallCoverageOverrides = new();
    private List<bool> recallRevealed = new();
    private List<string> recallRepairDrafts = new();
    private List<bool> recallRepairing = new();
    private List<int?> recallRepairIdeaIndices = new();
    private HashSet<string> recallRequeuedQuestionIDs = new(StringComparer.Ordinal);
    private int baseQuestionCount;
    private string sessionSnapshotID = NewID();
    private DateTime? sessionTargetDate;
    private PracticeRatingUndoMetadata? lastRatingUndo;
    private Delayed? autosave;
    private bool awaitingCompletion;

    private PracticeTopicPack? sessionPack;
    private List<PracticeQuestion> sessionQuestions = new();
    private List<PracticeScoredAnswer> sessionAnswers = new();
    private DateTime sessionStartedAt;
    private int questionIndex;
    private int viewingIndex;
    private bool usedHelp;
    private bool inSession;
    private bool historyVisible;

    public PracticeController(PracticeView view, IAppShell shell, PracticeStore? store = null, IPracticeRecorder? recorder = null, Random? random = null, PracticeBankFile? bank = null)
    {
        View = view;
        this.shell = shell;
        this.store = store ?? PracticeStore.Shared;
        this.recorder = recorder;
        this.random = random ?? new Random();
        this.bank = bank ?? PracticeBankLoader.Load();
        sessionStartedAt = Now();
        BuildUI();
        ReloadHistory();
        RefreshSelectionStatus();
    }

    public PracticeView View { get; }

    /// Goes back to the live interview tab.
    public Action? LeaveToTimeline { get; set; }

    /// Gives a control the keyboard, when the practice tab is the one on screen.
    public Action<Control?>? RequestInputFocus { get; set; }

    public Func<bool>? CanPresentPracticeUI { get; set; }

    public Func<MicrophoneLease?>? AcquireMicrophone { get; set; }

    public Func<MicrophoneLease, bool>? OwnsMicrophone { get; set; }

    public Action<MicrophoneLease>? ReleaseMicrophone { get; set; }

    /// The client that transcribes and judges answers, or null when there is no Groq key.
    public Func<PracticeGroqClient?> MakeClient { get; set; } = () =>
        ApiKeyManager.Shared.GetKey(ApiKeyType.Groq) is { Length: > 0 } key ? new PracticeGroqClient(key) : null;

    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public bool HasActiveSession => inSession;

    public bool IsHistoryVisible => historyVisible;

    private string? CardPackID => Practice.Role(selectedRoleID)?.CardPackID;

    private string CardDeckTitle => Practice.Role(selectedRoleID)?.Title ?? "Cards";

    private bool IsCardMode => CardPackID is not null;

    private bool IsRecallMode => !IsCardMode && selectedMode != PracticeRunMode.Interview;

    private string SelectedModeTitle => IsCardMode ? CardModeTitle(selectedMode) : selectedMode.Title();

    private string SessionModeTitle => IsCardMode
        ? SelectedModeTitle.ToUpperInvariant()
        : selectedMode switch
        {
            PracticeRunMode.Learn => "ACTIVE RECALL",
            PracticeRunMode.Rehearse => "VOICE REHEARSAL",
            _ => "INTERVIEW",
        };

    private bool WantsEntirePool => IsCardMode && selectedCount == 0;

    private static string NewID() => Guid.NewGuid().ToString().ToUpperInvariant();

    private static string CardModeTitle(PracticeRunMode mode) => mode == PracticeRunMode.Interview ? "Full Interview" : "Study Cards";

    private static T At<T>(List<T> list, int index, T fallback) => index >= 0 && index < list.Count ? list[index] : fallback;

    private static void Put<T>(List<T> list, int index, T value)
    {
        if (index >= 0 && index < list.Count) list[index] = value;
    }

    private static string Key(PracticeQuestion question) => Practice.RecallReviewKey(question.PackId, question.Id);

    /// A share as a whole percentage, rounded as the Mac's `%.0f` rounds it.
    private static string Percent(double fraction) => Math.Round(fraction * 100, MidpointRounding.ToEven).ToString("0", CultureInfo.InvariantCulture);

    private void SetStatus(string text) => View.StatusText = text;

    // MARK: - Coming and going

    public void Activate()
    {
        ReloadHistory();
        if (!inSession) UpdateIdlePlan();
        Relayout();
        Dispatcher.UIThread.Post(FocusResponseIfAppropriate);
    }

    public void Deactivate()
    {
        var shouldTranscribe = isRecording;
        StopRecording();
        if (shouldTranscribe) TranscribeRecording();
        if (inSession) SaveSessionSnapshot();
    }

    public void ShutdownMicrophone() => StopRecording();

    /// Saves what is unfinished before the app goes. False when it could not be saved.
    public bool PrepareForApplicationTermination()
    {
        autosave?.Dispose();
        recordingPermissionGeneration += 1;
        transcriptionGeneration += 1;
        isTranscribing = false;
        isScoringInterview = false;
        StopRecording();
        if (IsRecallMode) Put(recallDrafts, questionIndex, View.RecallCard.ResponseView.Text ?? "");
        else if (!IsCardMode && selectedMode == PracticeRunMode.Interview) Put(recallDrafts, questionIndex, View.QuestionCard.AnswerView.Text ?? "");
        return !inSession || SaveSessionSnapshot();
    }

    /// Asks what to do with an unfinished session before the window closes.
    public async Task<bool> ShouldAllowWindowCloseAsync()
    {
        if (!inSession) return true;
        if (isTranscribing || isScoringInterview)
        {
            await shell.AskAsync(new AppQuestion(
                isTranscribing ? "Finishing transcription" : "Finishing answer scoring",
                "Wait for this Practice result to finish so it can be saved safely.",
                new[] { "Keep Practice Open" }));
            return false;
        }
        if (isRecording)
        {
            await shell.AskAsync(new AppQuestion(
                "Finish the spoken answer first",
                "Stop recording so Practice can transcribe and save the answer before closing.",
                new[] { "Continue Recording" }));
            FocusResponseIfAppropriate();
            return false;
        }
        var choice = await shell.AskAsync(new AppQuestion(
            "Close Interview Master?",
            "Your unfinished Practice session can be saved for later or discarded.",
            new[] { "Save & Close", "Cancel", "Discard" }));
        switch (choice)
        {
            case 0:
                if (PrepareForApplicationTermination()) return true;
                SetStatus("Could not save this Practice session. The app will stay open.");
                FocusResponseIfAppropriate();
                return false;
            case 2:
                ResetIdle("Practice session discarded.");
                return true;
            default:
                FocusResponseIfAppropriate();
                return false;
        }
    }

    public void FocusResponseIfAppropriate()
    {
        Control? responder;
        if (!inSession)
        {
            responder = View.QuestionCard.Resume.IsVisible ? View.QuestionCard.Resume : View.Start;
        }
        else if (IsCardMode)
        {
            responder = At(recallDrafts, viewingIndex, "").Length > 0 ? View.ChoiceCard.Submit : View.ChoiceCard.OptionButtons.FirstOrDefault();
        }
        else if (awaitingCompletion)
        {
            responder = lastRatingUndo is null ? View.EndHeader : View.Undo;
        }
        else if (IsRecallMode)
        {
            if (reviewing is null)
            {
                responder = selectedMode == PracticeRunMode.Rehearse && At(recallDrafts, questionIndex, "x").Trim().Length == 0
                    ? View.RecallCard.Record
                    : View.RecallCard.ResponseView;
            }
            else
            {
                responder = At(recallRepairing, questionIndex, false) ? View.RecallCard.GapRepairResponder : View.RecallCard.Again;
            }
        }
        else
        {
            responder = View.QuestionCard.AnswerView;
        }
        RequestInputFocus?.Invoke(responder);
    }

    /// What Ctrl+Enter does: start, reveal, rate, submit or finish, whichever is next.
    public void PerformPrimaryAction()
    {
        if (!inSession)
        {
            _ = StartRunAsync();
            return;
        }
        if (isTranscribing)
        {
            SetStatus("Finishing the current transcription…");
            return;
        }
        if (IsCardMode)
        {
            AdvanceCardQuestion();
            return;
        }
        if (awaitingCompletion)
        {
            FinishRun();
            return;
        }
        if (selectedMode == PracticeRunMode.Interview)
        {
            SubmitAnswer();
            return;
        }
        if (At(recallRepairing, questionIndex, false))
        {
            CompleteGapRepair(View.RecallCard.RepairDraft);
            return;
        }
        if (reviewing is null) RevealRecallAnswer();
        else RateRecall(PracticeRecallRating.GotIt);
    }

    // MARK: - Setting up

    private void BuildUI()
    {
        var card = View.QuestionCard;
        var recall = View.RecallCard;
        View.History.Click += ToggleHistory;
        View.Leave.Click += LeaveToTimelineTapped;
        View.EndHeader.Click += () => _ = EndRunAsync();
        View.Start.Click += () => _ = StartRunAsync();
        View.Undo.Click += UndoLastRating;
        View.PracticeWeak.Click += () => _ = StartWeakPracticeAsync();

        fillingControls = true;
        View.Role.ItemsSource = PracticeRole.All.Select(role => role.Title).ToList();
        View.Role.SelectedIndex = 0;
        fillingControls = false;
        View.Role.SelectionChanged += (_, _) => RoleChanged();
        View.Mode.SelectionChanged += (_, _) => ModeChanged();
        View.Count.SelectionChanged += (_, _) => CountChanged();

        ApplyRoleSelection();
        ConfigureModeChoices();
        RebuildTopicChips();
        ConfigureCountChoices();

        var tomorrow = DateTime.Today.AddDays(1);
        View.TargetDatePicker.Minimum = tomorrow;
        View.TargetDatePicker.Date = DateTime.Today.AddDays(14);
        View.TargetDateToggle.Toggled += TargetDateToggled;
        View.TargetDatePicker.Changed += TargetDateChanged;

        recall.ResponseView.TextChanged += (_, _) => TextDidChange(recall.ResponseView);
        card.AnswerView.TextChanged += (_, _) => TextDidChange(card.AnswerView);
        recall.ConfidenceSelected += SetCurrentRecallConfidence;
        recall.CoverageChanged += SetCoverageOverride;
        recall.GapRepairSubmitted += CompleteGapRepair;
        recall.GapRepairDraftChanged += UpdateGapRepairDraft;
        View.ChoiceCard.Selected += SelectCardOption;
        View.ChoiceCard.Previous.Click += PreviousCardQuestion;
        View.ChoiceCard.Submit.Click += SubmitAnswer;

        card.Record.Click += ToggleRecord;
        card.Submit.Click += SubmitAnswer;
        card.End.Click += () => _ = EndRunAsync();
        card.Resume.Click += ResumeSavedSession;
        card.Contrast.Click += () => _ = StartContrastPracticeAsync();

        recall.Reveal.Click += RevealRecallAnswer;
        recall.Again.Click += () => RateRecall(PracticeRecallRating.Again);
        recall.Hard.Click += () => RateRecall(PracticeRecallRating.Hard);
        recall.GotIt.Click += () => RateRecall(PracticeRecallRating.GotIt);
        recall.Edit.Click += BeginGapRepair;
        recall.DontKnow.Click += RevealRecallWithoutAnswer;
        recall.Record.Click += ToggleRecord;

        UpdateModeDescription();
        UpdateSessionControls();
    }

    private void Relayout()
    {
        View.InSession = inSession;
        View.CardMode = IsCardMode;
        View.HistoryVisible = historyVisible;
        View.UndoAvailable = lastRatingUndo is not null;
        View.Refresh();
    }

    private void LeaveToTimelineTapped()
    {
        if (inSession)
        {
            _ = EndRunAsync();
            return;
        }
        Deactivate();
        LeaveToTimeline?.Invoke();
    }

    public void ToggleHistory()
    {
        historyVisible = !historyVisible;
        PracticeUi.SetTitle(View.History, historyVisible ? "Close history" : HistoryButtonTitle());
        if (historyVisible) ReloadHistory();
        Relayout();
    }

    private string HistoryButtonTitle()
    {
        var count = store.AllRuns().Count;
        return count == 0 ? "History" : $"History ({count})";
    }

    private void RoleChanged()
    {
        if (fillingControls) return;
        var index = View.Role.SelectedIndex;
        selectedRoleID = index >= 0 && index < PracticeRole.All.Count ? PracticeRole.All[index].Id : PracticeRole.AIEngineer.Id;
        ApplyRoleSelection();
        ConfigureModeChoices();
        ConfigureCountChoices();
        UpdateModeDescription();
        RebuildTopicChips();
        Relayout();
        ReloadHistory();
        RefreshSelectionStatus();
    }

    private void TopicToggled(PracticeCheckBox sender)
    {
        if (sender.IsChecked)
        {
            selectedGroupIDs.Add(sender.Id);
        }
        else
        {
            selectedGroupIDs.Remove(sender.Id);
            if (selectedGroupIDs.Count == 0)
            {
                selectedGroupIDs.Add(sender.Id);
                sender.IsChecked = true;
            }
        }
        RefreshSelectionStatus();
    }

    private void ApplyRoleSelection()
    {
        var available = AllAvailableTopicIDs();
        var role = Practice.Role(selectedRoleID) ?? PracticeRole.AIEngineer;
        selectedGroupIDs = new HashSet<string>(Practice.GroupIDs(role, available), StringComparer.Ordinal);
        if (!IsCardMode && selectedGroupIDs.Count == 0 && available.Count > 0) selectedGroupIDs = new HashSet<string>(StringComparer.Ordinal) { available[0] };
    }

    private List<string> AllAvailableTopicIDs() => Practice.BankGroups(bank).Select(group => group.Id).ToList();

    private int PlannedRunCount(IReadOnlyCollection<PracticeQuestion> pool) => WantsEntirePool ? pool.Count : Math.Min(selectedCount, pool.Count);

    private void ConfigureCountChoices()
    {
        var titles = CountChoices.Select(choice => choice.Title).ToList();
        countChoices = CountChoices.Select(choice => choice.Count).ToList();
        if (IsCardMode)
        {
            titles.Add("All");
            countChoices.Add(0);
        }
        else if (selectedCount == 0)
        {
            selectedCount = 10;
        }
        fillingControls = true;
        View.Count.ItemsSource = titles;
        View.Count.SelectedIndex = countChoices.IndexOf(selectedCount);
        fillingControls = false;
    }

    private void CountChanged()
    {
        if (fillingControls) return;
        var index = View.Count.SelectedIndex;
        if (index >= 0 && index < countChoices.Count) selectedCount = countChoices[index];
        RefreshSelectionStatus();
    }

    private List<(string Id, string Title)> SelectableTopics()
    {
        var role = Practice.Role(selectedRoleID) ?? PracticeRole.AIEngineer;
        var allowed = Practice.GroupIDs(role, AllAvailableTopicIDs());
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in Practice.BankGroups(bank)) titles[group.Id] = group.Title;
        var topics = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = Practice.InterviewTopicOrder.Concat(allowed.Where(id => !Practice.InterviewTopicOrder.Contains(id)));
        foreach (var id in ordered)
        {
            if (allowedSet.Contains(id) && seen.Add(id)) topics.Add((id, titles.GetValueOrDefault(id, id)));
        }
        return topics;
    }

    private void RebuildTopicChips() =>
        View.SetTopics(SelectableTopics().Select(topic => (topic.Id, topic.Title, selectedGroupIDs.Contains(topic.Id))), TopicToggled);

    private void SyncTopicChips()
    {
        foreach (var button in View.TopicButtons) button.IsChecked = selectedGroupIDs.Contains(button.Id);
    }

    private void SetSetupEnabled(bool on)
    {
        PracticeUi.Enable(View.Role, on);
        PracticeUi.Enable(View.Mode, on);
        PracticeUi.Enable(View.Count, on);
        foreach (var button in View.TopicButtons) button.IsEnabled = on;
    }

    private string GroupTitle(PracticeQuestion question)
    {
        var fallback = bank.Packs
            .Select(pack => pack.Groups.FirstOrDefault(group => group.Id == question.GroupId)?.Title)
            .FirstOrDefault(title => title is not null) ?? question.GroupId;
        return Practice.GroupDisplayTitle(question.GroupId, fallback);
    }

    private List<PracticeQuestion> CurrentPool()
    {
        if (IsCardMode)
        {
            return bank.Packs.Where(pack => pack.Id == CardPackID).SelectMany(pack => pack.Questions)
                .Where(question => selectedGroupIDs.Contains(question.GroupId) && question.Card?.IsValid == true)
                .ToList();
        }
        return Practice.QuestionPool(bank, selectedGroupIDs);
    }

    private void RefreshSelectionStatus()
    {
        var pool = CurrentPool();
        var runCount = PlannedRunCount(pool);
        var topicCount = pool.Select(question => question.GroupId).Distinct(StringComparer.Ordinal).Count();
        var perTopic = topicCount == 0 ? 0 : (runCount + topicCount - 1) / topicCount;
        var names = SelectableTopics().Where(topic => selectedGroupIDs.Contains(topic.Id)).Select(topic => topic.Title).ToList();
        var roleTitle = Practice.Role(selectedRoleID)?.Title ?? "Practice";
        var minutesPerQuestion = selectedMode == PracticeRunMode.Learn ? 1.0 : 2.0;
        var estimatedMinutes = Math.Max(1, (int)Math.Ceiling(runCount * minutesPerQuestion));
        if (runCount < topicCount)
        {
            SetStatus($"{roleTitle} · {SelectedModeTitle} · {runCount} of {topicCount} topics · ~{estimatedMinutes} min");
            View.StatusTip = $"Increase Questions to at least {topicCount} to cover every selected topic. Selected: {string.Join(", ", names)}";
        }
        else
        {
            SetStatus($"{roleTitle} · {SelectedModeTitle} · {topicCount} topics · {runCount} questions (~{perTopic} per topic) · ~{estimatedMinutes} min");
            View.StatusTip = string.Join(", ", names);
        }
        if (!inSession) UpdateIdlePlan();
    }

    private void ModeChanged()
    {
        if (fillingControls) return;
        var index = View.Mode.SelectedIndex;
        selectedMode = index >= 0 && index < modeChoices.Count ? modeChoices[index] : PracticeRunMode.Learn;
        UpdateModeDescription();
        RefreshSelectionStatus();
    }

    private void ConfigureModeChoices()
    {
        modeChoices = IsCardMode
            ? new List<PracticeRunMode> { PracticeRunMode.Learn, PracticeRunMode.Interview }
            : PracticeRunModes.AllCases.ToList();
        if (!modeChoices.Contains(selectedMode)) selectedMode = PracticeRunMode.Learn;
        fillingControls = true;
        View.Mode.ItemsSource = modeChoices.Select(mode => IsCardMode ? CardModeTitle(mode) : mode.Title()).ToList();
        View.Mode.SelectedIndex = modeChoices.IndexOf(selectedMode);
        fillingControls = false;
        ToolTip.SetTip(View.Mode, IsCardMode
            ? "Study Cards reveals answers after a choice. Full Interview reveals results only when the run is finished."
            : "Choose active recall, spoken rehearsal, or a free-response interview.");
    }

    private void SelectMode(PracticeRunMode mode)
    {
        selectedMode = mode;
        fillingControls = true;
        View.Mode.SelectedIndex = modeChoices.IndexOf(mode);
        fillingControls = false;
    }

    private void UpdateModeDescription()
    {
        if (IsCardMode)
        {
            View.SubtitleText = selectedMode == PracticeRunMode.Interview
                ? $"{CardDeckTitle}: choose answers now; review correctness and explanations at the end."
                : $"{CardDeckTitle}: choose one answer, learn why, then continue.";
            return;
        }
        View.SubtitleText = selectedMode switch
        {
            PracticeRunMode.Learn => "Active Recall lets you answer first, compare key ideas, repair gaps, and schedule the next review.",
            PracticeRunMode.Rehearse => "Voice Rehearsal practices a spoken answer, then uses Key Ideas and one-gap repair before retrying.",
            _ => "Interview mode uses typed or spoken answers and scores them without showing learning help.",
        };
    }

    private DateTime PickedTargetDate => (View.TargetDatePicker.Date + targetTimeOfDay).ToUniversalTime();

    private void TargetDateToggled()
    {
        var enabled = View.TargetDateToggle.IsChecked;
        View.TargetDatePicker.IsEnabled = enabled;
        sessionTargetDate = enabled ? PickedTargetDate : null;
        RefreshSelectionStatus();
        UpdateIdlePlan();
    }

    private void TargetDateChanged()
    {
        if (View.TargetDateToggle.IsChecked) sessionTargetDate = PickedTargetDate;
        UpdateIdlePlan();
    }

    // MARK: - The plan, history and saved sessions

    private void UpdateIdlePlan()
    {
        if (inSession) return;
        var card = View.QuestionCard;
        View.ChoiceCard.IsVisible = false;
        if (IsCardMode)
        {
            card.ShowIdle();
            card.TitleLabel.Text = CardDeckTitle;
            card.SourceLabel.Text = "Anthropic / Claude · original curriculum practice";
            card.SetQuestionText($"{CurrentPool().Count} cards in the selected domains.\n\nStudy Cards: choose one of four answers, then see the explanation.\n\nFull Interview: answers stay hidden until you finish.\n\nScored locally. No API key needed.");
            card.Resume.IsVisible = store.InProgressSession() is not null;
            card.Contrast.IsVisible = false;
            PracticeUi.SetTitle(View.Start, selectedMode == PracticeRunMode.Interview ? "Start interview" : "Study cards");
            PracticeUi.Enable(View.Start, CurrentPool().Count > 0);
            Relayout();
            return;
        }
        var runs = store.AllRuns();
        var latest = Practice.LatestRecallAnswers(runs);
        var attempts = Practice.RecallAttempts(runs);
        var pool = CurrentPool();
        var plan = Practice.TodaysPlan(
            pool, latest, attempts, Now(),
            maximumQuestions: Math.Max(1, PlannedRunCount(pool)),
            estimatedSecondsPerQuestion: selectedMode == PracticeRunMode.Rehearse ? 120 : 60);
        string? resumable = null;
        if (store.InProgressSession() is { CanResume: true } snapshot)
        {
            resumable = $"{SnapshotModeTitle(snapshot)} · question {snapshot.CurrentQuestionIndex + 1} of {snapshot.BaseQuestionCount}";
        }
        if (selectedMode == PracticeRunMode.Interview)
        {
            card.ShowIdle();
            card.TitleLabel.Text = "Mock interview";
            card.SourceLabel.Text = "Scored free response · no learning help";
            card.SetQuestionText("Practice answering under interview conditions by typing or speaking, then review the saved report.");
            card.Resume.IsVisible = resumable is not null;
            card.Contrast.IsVisible = false;
            PracticeUi.SetTitle(View.Start, "Start interview");
            Relayout();
            return;
        }
        card.ShowTodayPlan(plan, resumable);
        card.Resume.IsVisible = resumable is not null;
        card.Contrast.IsVisible = true;
        PracticeUi.Enable(card.Contrast, Practice.ContrastPairs(pool, latest, attempts, Now(), limit: 1).Count > 0);
        PracticeUi.SetTitle(View.Start, plan.RecommendedCount > 0 ? "Start today’s plan" : "Start practice");
        Relayout();
    }

    private static string SnapshotModeTitle(PracticeSessionSnapshot snapshot) =>
        PracticeRole.CardRole(snapshot.PackId) is null ? snapshot.Mode.Title() : CardModeTitle(snapshot.Mode);

    public void ResumeSavedSession()
    {
        if (store.InProgressSession() is not { CanResume: true } snapshot)
        {
            SetStatus("No resumable Practice session was found.");
            UpdateIdlePlan();
            return;
        }
        RestoreSession(snapshot, persist: false);
    }

    private void RestoreSession(PracticeSessionSnapshot snapshot, bool persist)
    {
        var questionsByKey = new Dictionary<string, PracticeQuestion>(StringComparer.Ordinal);
        foreach (var question in bank.Packs.SelectMany(pack => pack.Questions)) questionsByKey[Key(question)] = question;
        var restoringCards = PracticeRole.CardRole(snapshot.PackId) is not null;
        var restoredQuestions = new List<PracticeQuestion>();
        foreach (var state in snapshot.QuestionStates)
        {
            var question = restoringCards
                ? Practice.RestoreCardQuestion(state)
                : questionsByKey.GetValueOrDefault(Practice.RecallReviewKey(state.Reference.PackId, state.Reference.QuestionId));
            if (question is not null) restoredQuestions.Add(question);
        }
        if (restoredQuestions.Count != snapshot.QuestionStates.Count)
        {
            SetStatus("This saved session references questions that are no longer available.");
            return;
        }
        StopRecording();
        sessionSnapshotID = snapshot.Id;
        selectedRoleID = snapshot.RoleId ?? selectedRoleID;
        if (!snapshot.GroupIds.IsEmpty) selectedGroupIDs = new HashSet<string>(snapshot.GroupIds, StringComparer.Ordinal);
        selectedMode = snapshot.Mode;
        sessionQuestions = restoredQuestions;
        sessionAnswers = snapshot.Answers.ToList();
        sessionStartedAt = snapshot.StartedAt;
        baseQuestionCount = snapshot.BaseQuestionCount;
        questionIndex = Math.Min(Math.Max(0, snapshot.CurrentQuestionIndex), Math.Max(0, restoredQuestions.Count - 1));
        viewingIndex = questionIndex;
        recallRequeuedQuestionIDs = new HashSet<string>(snapshot.RequeuedQuestionKeys, StringComparer.Ordinal);
        lastRatingUndo = snapshot.LastRatingUndo;
        awaitingCompletion = snapshot.CurrentQuestionIndex == snapshot.QuestionStates.Count - 1
            && snapshot.Answers.Count >= snapshot.QuestionStates.Count;
        sessionTargetDate = snapshot.TargetDate;
        View.TargetDateToggle.IsChecked = snapshot.TargetDate is not null;
        View.TargetDatePicker.IsEnabled = snapshot.TargetDate is not null;
        if (snapshot.TargetDate is { } targetDate) View.TargetDatePicker.Date = targetDate.ToLocalTime();
        recallDrafts = snapshot.QuestionStates.Select(state => state.DraftResponse).ToList();
        recallUsedHelp = snapshot.QuestionStates.Select(state => state.UsedHelp).ToList();
        recallResponses = snapshot.QuestionStates.Select(state => state.RecallResponse).ToList();
        recallConfidences = snapshot.QuestionStates.Select(state => state.Confidence).ToList();
        recallCoverageOverrides = snapshot.QuestionStates.Select(state => state.CoverageOverrides.ToList()).ToList();
        recallRevealed = snapshot.QuestionStates.Select(state => state.HasRevealedKeyIdeas).ToList();
        recallRepairDrafts = snapshot.QuestionStates.Select(state => state.RepairDraft).ToList();
        recallRepairing = snapshot.QuestionStates.Select(state => state.IsRepairingGap).ToList();
        recallRepairIdeaIndices = snapshot.QuestionStates.Select(state => state.RepairIdeaIndex).ToList();
        sessionPack = new PracticeTopicPack
        {
            Id = snapshot.PackId,
            Title = snapshot.PackTitle,
            Blurb = "",
            Groups = bank.Packs.FirstOrDefault(pack => pack.Id == snapshot.PackId)?.Groups ?? ValueList<PracticeGroup>.Empty,
            Questions = restoredQuestions,
        };
        inSession = true;
        isScoringInterview = false;
        fillingControls = true;
        View.Role.SelectedIndex = Math.Max(0, PracticeRole.All.ToList().FindIndex(role => role.Id == selectedRoleID));
        fillingControls = false;
        ConfigureModeChoices();
        UpdateModeDescription();
        RebuildTopicChips();
        SyncTopicChips();
        SetSetupEnabled(false);
        RestoreLearnPhase(questionIndex);
        DisplayQuestion(questionIndex);
        SetStatus(SessionPositionText(sessionQuestions[questionIndex]));
        ShowProgress();
        UpdateSessionControls();
        Relayout();
        Dispatcher.UIThread.Post(FocusResponseIfAppropriate);
        if (persist) SaveSessionSnapshot();
    }

    private void ShowProgress()
    {
        View.ModePillText = SessionModeTitle;
        View.Progress.Total = Math.Max(1, baseQuestionCount);
        View.Progress.Completed = Math.Min(baseQuestionCount, questionIndex);
        View.Progress.CurrentIndex = questionIndex;
        View.Progress.QueuedReviews = Math.Max(0, sessionQuestions.Count - baseQuestionCount);
    }

    public void UndoLastRating()
    {
        if (MakeSessionSnapshot() is not { LastRatingUndo: { } undo } snapshot
            || undo.AnswerCountBeforeRating < 0 || undo.AnswerCountBeforeRating >= snapshot.Answers.Count
            || Practice.SnapshotAfterUndoingLastRating(snapshot, Now()) is not { } restored)
        {
            SetStatus("There is no rating to undo.");
            return;
        }
        var ratedAnswer = snapshot.Answers[undo.AnswerCountBeforeRating];
        if (ratedAnswer.RecallRating == PracticeRecallRating.Again && undo.RequeuedQuestionKeysBeforeRating is null)
        {
            var key = Practice.RecallReviewKey(undo.Question.PackId, undo.Question.QuestionId);
            restored = restored with { RequeuedQuestionKeys = restored.RequeuedQuestionKeys.Where(existing => existing != key).ToList() };
        }
        RestoreSession(restored, persist: true);
        SetStatus("Last rating undone. Review and choose again.");
    }

    private PracticeSessionQuestionState? MakeSessionQuestionState(int index)
    {
        if (index < 0 || index >= sessionQuestions.Count) return null;
        var question = sessionQuestions[index];
        string draft;
        if (IsCardMode || IsRecallMode) draft = At(recallDrafts, index, "");
        else if (index == questionIndex) draft = View.QuestionCard.AnswerView.Text ?? "";
        else draft = index < sessionAnswers.Count ? sessionAnswers[index].Answer : "";
        return new PracticeSessionQuestionState(new PracticeQuestionReference(question.PackId, question.Id))
        {
            DraftResponse = draft,
            RecallResponse = At(recallResponses, index, null),
            Confidence = At(recallConfidences, index, null),
            HasRevealedKeyIdeas = At(recallRevealed, index, false),
            CoverageOverrides = At(recallCoverageOverrides, index, new List<PracticeCoverageOverride>()),
            UsedHelp = At(recallUsedHelp, index, false),
            RepairDraft = At(recallRepairDrafts, index, ""),
            IsRepairingGap = At(recallRepairing, index, false),
            RepairIdeaIndex = At(recallRepairIdeaIndices, index, null),
            CardQuestion = IsCardMode ? question : null,
        };
    }

    private PracticeSessionSnapshot? MakeSessionSnapshot()
    {
        if (!inSession || sessionPack is not { } pack) return null;
        var states = new List<PracticeSessionQuestionState>();
        for (var index = 0; index < sessionQuestions.Count; index++)
        {
            if (MakeSessionQuestionState(index) is { } state) states.Add(state);
        }
        if (states.Count != sessionQuestions.Count) return null;
        return new PracticeSessionSnapshot(
            pack.Id, pack.Title, selectedMode, states, sessionStartedAt, Now(),
            id: sessionSnapshotID,
            roleId: selectedRoleID,
            groupIds: selectedGroupIDs.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            baseQuestionCount: baseQuestionCount,
            currentQuestionIndex: questionIndex,
            answers: sessionAnswers,
            requeuedQuestionKeys: recallRequeuedQuestionIDs.OrderBy(key => key, StringComparer.Ordinal).ToList(),
            lastRatingUndo: lastRatingUndo,
            targetDate: sessionTargetDate);
    }

    private bool SaveSessionSnapshot()
    {
        if (MakeSessionSnapshot() is not { } snapshot) return false;
        if (!store.SaveInProgressSession(snapshot))
        {
            SetStatus("Could not autosave this Practice session.");
            return false;
        }
        View.QuestionCard.Resume.IsVisible = false;
        return true;
    }

    private void ScheduleSessionAutosave()
    {
        autosave?.Dispose();
        autosave = Delayed.After(0.25, () => SaveSessionSnapshot());
    }

    private void TextDidChange(TextBox editor)
    {
        if (!inSession || editor.IsReadOnly) return;
        var text = editor.Text ?? "";
        var recallEdit = IsRecallMode && editor == View.RecallCard.ResponseView;
        var interviewEdit = !IsCardMode && selectedMode == PracticeRunMode.Interview && editor == View.QuestionCard.AnswerView;
        if (!recallEdit && !interviewEdit) return;
        // The editor also reports the text the app put there; only the person's own edits are drafts.
        if (questionIndex < 0 || questionIndex >= recallDrafts.Count || recallDrafts[questionIndex] == text) return;
        recallDrafts[questionIndex] = text;
        ScheduleSessionAutosave();
    }

    private string FormatReport(PracticeRunRecord run)
    {
        if (PracticeRole.CardRole(run.PackId) is { } cardRole)
        {
            var lines = new List<string>
            {
                $"{cardRole.Title} · {CardModeTitle(run.Mode ?? PracticeRunMode.Learn)} · {Percent(run.OverallScore)}% · {run.Answers.Count} questions",
            };
            var topics = run.Answers.GroupBy(answer => answer.CardResult?.Topic ?? cardRole.Title, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal);
            foreach (var topic in topics) lines.Add($"{topic.Key}: {topic.Count(answer => answer.FinalScore == 1)}/{topic.Count()}");
            for (var index = 0; index < run.Answers.Count; index++)
            {
                var answer = run.Answers[index];
                if (answer.CardResult is not { } card) continue;
                lines.Add($"\n{index + 1}. {answer.Question}\nYour choice: {answer.Answer}\nCorrect answer: {card.CorrectAnswer}\n{card.Explanation}");
            }
            return string.Join("\n", lines);
        }
        var mode = run.Mode?.Title() ?? "Practice";
        var report = new List<string> { $"Report · {mode} · {run.PackTitle} · {Percent(run.OverallScore)}% · {run.HelpedCount} assisted" };
        for (var index = 0; index < run.Answers.Count; index++)
        {
            var answer = run.Answers[index];
            var mark = answer.UsedHelp ? "  ASSISTED" : "";
            report.Add($"{index + 1}. {Percent(answer.FinalScore)}% (raw {Percent(answer.RawScore)}%){mark} — {answer.Feedback}");
        }
        return string.Join("\n", report);
    }

    private void ReloadHistory()
    {
        var runs = store.AllRuns().OrderByDescending(run => run.FinishedAt).ToList();
        if (IsCardMode)
        {
            var recent = runs.Where(run => run.PackId == CardPackID).Take(10).ToList();
            var lines = new List<string> { $"{CardDeckTitle} · last 10 tests" };
            foreach (var run in recent)
            {
                lines.Add($"{ShortDate(run.FinishedAt)} · {(run.Mode == PracticeRunMode.Interview ? "Interview" : "Study")} · {Percent(run.OverallScore)}% · {run.Answers.Count} questions");
            }
            if (recent.Count == 0) lines.Add($"No completed {CardDeckTitle} tests yet.");
            View.HistoryText = string.Join("\n", lines);
            PracticeUi.Enable(View.PracticeWeak, false);
            if (!historyVisible) PracticeUi.SetTitle(View.History, HistoryButtonTitle());
            return;
        }
        var latest = Practice.LatestRecallAnswers(runs);
        var attempts = Practice.RecallAttempts(runs);
        var summaries = Practice.TopicMasterySummaries(CurrentPool(), latest, attempts, Now())
            .OrderByDescending(summary => summary.DueCount)
            .ThenByDescending(summary => summary.LearningCount)
            .ThenBy(summary => summary.Topic.Title, StringComparer.Ordinal)
            .ToList();
        var mastery = new List<string> { "Topic mastery" };
        if (summaries.Count == 0)
        {
            mastery.Add("No practice data yet.");
        }
        else
        {
            foreach (var summary in summaries.Take(8))
            {
                var percent = (int)Math.Round(summary.MasteredFraction * 100, MidpointRounding.AwayFromZero);
                mastery.Add($"{summary.Topic.Title}  {percent}% solid · {summary.DueCount} due · {summary.LearningCount} learning");
            }
        }
        if (runs.Count > 0)
        {
            mastery.Add("");
            mastery.Add($"Latest · {ShortDate(runs[0].FinishedAt)} · {Percent(runs[0].OverallScore)}%");
        }
        View.HistoryText = string.Join("\n", mastery);
        PracticeUi.Enable(View.PracticeWeak, summaries.Any(summary => summary.DueCount > 0 || summary.LearningCount > 0));
        if (!historyVisible) PracticeUi.SetTitle(View.History, HistoryButtonTitle());
    }

    private static string ShortDate(DateTime date) => date.ToLocalTime().ToString("MMM d", CultureInfo.CurrentCulture);

    public async Task StartWeakPracticeAsync()
    {
        var runs = store.AllRuns();
        var latest = Practice.LatestRecallAnswers(runs);
        var attempts = Practice.RecallAttempts(runs);
        var now = Now();
        var weak = CurrentPool().Where(question =>
        {
            var state = Practice.MasteryState(latest.GetValueOrDefault(Key(question)), attempts.GetValueOrDefault(Key(question)), now);
            return state is PracticeMasteryState.Due or PracticeMasteryState.Learning;
        }).ToList();
        var selection = Practice.PrioritizedRecallQuestions(weak, latest, now).Take(5).ToList();
        if (selection.Count == 0)
        {
            SetStatus("No due or learning questions are available in the selected topics.");
            return;
        }
        if (!await ConfirmReplacingSavedSessionIfNeededAsync()) return;
        SelectMode(PracticeRunMode.Learn);
        UpdateModeDescription();
        selectedCount = 5;
        fillingControls = true;
        View.Count.SelectedIndex = countChoices.IndexOf(selectedCount);
        fillingControls = false;
        historyVisible = false;
        BeginSession(selection, "Weak concepts");
    }

    public async Task StartContrastPracticeAsync()
    {
        var pool = CurrentPool();
        var runs = store.AllRuns();
        var selection = Practice.ContrastQuestionSelection(
            pool, Practice.LatestRecallAnswers(runs), Practice.RecallAttempts(runs), Now(),
            pairLimit: Math.Max(1, Math.Min(3, selectedCount / 2)));
        if (selection.Count == 0)
        {
            SetStatus("No strong comparison pair is available in the selected topics.");
            return;
        }
        if (!await ConfirmReplacingSavedSessionIfNeededAsync()) return;
        SelectMode(PracticeRunMode.Learn);
        UpdateModeDescription();
        BeginSession(selection, "Contrast round");
    }
}
