using Avalonia.Controls;
using Avalonia.Threading;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Presentation;

public sealed partial class PracticeController
{
    private bool isRecording;
    private bool recordingPermissionPending;
    private bool isTranscribing;
    private bool isScoringInterview;
    private int recordingPermissionGeneration;
    private int transcriptionGeneration;
    private MicrophoneLease? microphoneLease;

    // MARK: - Starting

    public async Task StartRunAsync()
    {
        if (inSession) return;
        var pool = CurrentPool();
        if (pool.Count == 0)
        {
            SetStatus(IsCardMode ? $"No {CardDeckTitle} cards are available for the selected topics." : "Tick at least one topic.");
            return;
        }
        if (!await ConfirmReplacingSavedSessionIfNeededAsync()) return;
        var runs = store.AllRuns();
        var latestRecall = IsRecallMode ? Practice.LatestRecallAnswers(runs) : new Dictionary<string, PracticeScoredAnswer>();
        var recallAttempts = IsRecallMode ? Practice.RecallAttempts(runs) : new Dictionary<string, List<PracticeScoredAnswer>>();
        var selectionDate = Now();
        var selectionPool = pool;
        var desiredCount = PlannedRunCount(pool);
        if (IsRecallMode)
        {
            var actionable = pool.Where(question =>
                Practice.MasteryState(latestRecall.GetValueOrDefault(Key(question)), recallAttempts.GetValueOrDefault(Key(question)), selectionDate)
                != PracticeMasteryState.Solid).ToList();
            if (actionable.Count > 0)
            {
                selectionPool = actionable;
                desiredCount = Math.Min(
                    selectedCount,
                    Practice.TodaysPlan(actionable, latestRecall, recallAttempts, selectionDate, maximumQuestions: selectedCount).RecommendedCount);
            }
        }
        var groupIds = selectedGroupIDs.ToList();
        if (WantsEntirePool && selectedMode == PracticeRunMode.Learn)
        {
            BeginSession(Practice.CurriculumOrdered(pool), CardDeckTitle);
            return;
        }
        var recall = IsRecallMode;
        var selectedQuestions = Practice.InterviewOrderedSelection(
            selectionPool, desiredCount,
            question => Practice.TopicKey(question, groupIds),
            shuffle: questions => recall
                ? Practice.PrioritizedRecallQuestions(questions, latestRecall, selectionDate)
                : Shuffled(questions));
        BeginSession(selectedQuestions, IsCardMode ? CardDeckTitle : "Study book");
    }

    private List<T> Shuffled<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        for (var index = list.Count - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (list[index], list[other]) = (list[other], list[index]);
        }
        return list;
    }

    private async Task<bool> ConfirmReplacingSavedSessionIfNeededAsync()
    {
        if (inSession || store.InProgressSession() is not { CanResume: true } snapshot) return true;
        var choice = await shell.AskAsync(new AppQuestion(
            "Start a new Practice session?",
            $"A saved {SnapshotModeTitle(snapshot)} session is waiting at question {snapshot.CurrentQuestionIndex + 1} of {snapshot.BaseQuestionCount}.",
            new[] { "Start New", "Resume Saved", "Cancel" }));
        switch (choice)
        {
            case 0:
                if (store.ClearInProgressSession()) return true;
                SetStatus("Could not replace the saved session. Try again.");
                return false;
            case 1:
                ResumeSavedSession();
                return false;
            default:
                return false;
        }
    }

    private void BeginSession(IReadOnlyList<PracticeQuestion> questions, string sessionTitle)
    {
        if (questions.Count == 0)
        {
            SetStatus("No matching questions are available for this session.");
            return;
        }
        if (IsCardMode && !questions.All(question => question.PackId == CardPackID && question.Card?.IsValid == true))
        {
            SetStatus($"The {CardDeckTitle} deck could not be validated.");
            return;
        }
        StopRecording();
        sessionQuestions = questions.Select(question =>
            IsCardMode && question.Card is { } card
                ? question with { Card = card with { Options = Shuffled(card.Options) } }
                : question).ToList();
        var packID = CardPackID ?? "study-book";
        var study = bank.Packs.FirstOrDefault(pack => pack.Id == packID);
        sessionPack = new PracticeTopicPack
        {
            Id = packID,
            Title = $"{sessionTitle} · {sessionQuestions.Count}",
            Blurb = study?.Blurb ?? "",
            Groups = study?.Groups ?? ValueList<PracticeGroup>.Empty,
            Questions = sessionQuestions,
        };
        sessionAnswers = new List<PracticeScoredAnswer>();
        sessionStartedAt = Now();
        sessionSnapshotID = NewID();
        sessionTargetDate = !IsCardMode && View.TargetDateToggle.IsChecked ? PickedTargetDate : null;
        lastRatingUndo = null;
        awaitingCompletion = false;
        questionIndex = 0;
        viewingIndex = 0;
        baseQuestionCount = sessionQuestions.Count;
        inSession = true;
        isScoringInterview = false;
        reviewing = null;
        var count = sessionQuestions.Count;
        recallDrafts = Enumerable.Repeat("", count).ToList();
        recallRequeuedQuestionIDs = new HashSet<string>(StringComparer.Ordinal);
        if (IsRecallMode)
        {
            recallUsedHelp = Enumerable.Repeat(false, count).ToList();
            recallResponses = Enumerable.Repeat<PracticeRecallResponse?>(null, count).ToList();
            recallConfidences = Enumerable.Repeat<PracticeRecallConfidence?>(null, count).ToList();
            recallCoverageOverrides = Enumerable.Range(0, count).Select(_ => new List<PracticeCoverageOverride>()).ToList();
            recallRevealed = Enumerable.Repeat(false, count).ToList();
            recallRepairDrafts = Enumerable.Repeat("", count).ToList();
            recallRepairing = Enumerable.Repeat(false, count).ToList();
            recallRepairIdeaIndices = Enumerable.Repeat<int?>(null, count).ToList();
        }
        else
        {
            recallUsedHelp = new();
            recallResponses = new();
            recallConfidences = new();
            recallCoverageOverrides = new();
            recallRevealed = new();
            recallRepairDrafts = new();
            recallRepairing = new();
            recallRepairIdeaIndices = new();
        }
        PracticeUi.Enable(View.Start, false);
        SetSetupEnabled(false);
        UpdateSessionControls();
        PresentCurrentQuestion();
        SaveSessionSnapshot();
    }

    // MARK: - Study cards

    private void DisplayCard(int index)
    {
        if (index < 0 || index >= sessionQuestions.Count || index >= recallDrafts.Count) return;
        var question = sessionQuestions[index];
        var selected = recallDrafts[index].Length == 0 ? null : recallDrafts[index];
        var feedback = Practice.CardFeedback(question, selected, selectedMode);
        View.QuestionCard.IsVisible = false;
        View.RecallCard.IsVisible = false;
        View.ChoiceCard.IsVisible = true;
        View.ChoiceCard.DeckTitle = CardDeckTitle;
        View.ChoiceCard.Display(
            question, index, sessionQuestions.Count, selected, feedback,
            allowsSelection: inSession && (selectedMode == PracticeRunMode.Interview || selected is null));
        SetStatus($"Question {index + 1} of {sessionQuestions.Count} · {sessionAnswers.Count} answered");
        UpdateSessionControls();
        Relayout();
    }

    public void SelectCardOption(string optionID)
    {
        if (!inSession || !IsCardMode || viewingIndex > questionIndex || viewingIndex < 0 || viewingIndex >= recallDrafts.Count) return;
        if (selectedMode != PracticeRunMode.Interview && recallDrafts[viewingIndex].Length > 0) return;
        if (Practice.ScoreCard(sessionQuestions[viewingIndex], optionID) is not { } score) return;
        if (viewingIndex < sessionAnswers.Count) sessionAnswers[viewingIndex] = score;
        else if (viewingIndex == sessionAnswers.Count) sessionAnswers.Add(score);
        else return;
        recallDrafts[viewingIndex] = optionID;
        awaitingCompletion = sessionAnswers.Count == sessionQuestions.Count;
        DisplayCard(viewingIndex);
        SaveSessionSnapshot();
    }

    public void PreviousCardQuestion()
    {
        if (!inSession || !IsCardMode || viewingIndex <= 0) return;
        viewingIndex -= 1;
        DisplayCard(viewingIndex);
        FocusResponseIfAppropriate();
    }

    private void AdvanceCardQuestion()
    {
        if (!inSession || !IsCardMode || At(recallDrafts, viewingIndex, "").Length == 0) return;
        if (!SaveSessionSnapshot()) return;
        if (viewingIndex < questionIndex)
        {
            viewingIndex += 1;
            DisplayCard(viewingIndex);
        }
        else if (questionIndex + 1 < sessionQuestions.Count)
        {
            questionIndex += 1;
            PresentCurrentQuestion();
            SaveSessionSnapshot();
        }
        else
        {
            FinishRun();
        }
        FocusResponseIfAppropriate();
    }

    // MARK: - Showing the question

    private void UpdateSessionControls()
    {
        var active = inSession;
        var card = View.QuestionCard;
        var recall = View.RecallCard;
        var choice = View.ChoiceCard;
        card.Resume.IsVisible = !active && store.InProgressSession() is not null;
        card.Contrast.IsVisible = !active;
        card.Record.IsVisible = active && !IsRecallMode;
        card.Submit.IsVisible = active && !IsRecallMode;
        card.End.IsVisible = active && !IsRecallMode;
        PracticeUi.Enable(card.Record, active && !isTranscribing && !isScoringInterview);
        PracticeUi.Enable(card.Submit, active && !isTranscribing && !isScoringInterview);

        foreach (var button in new[] { recall.Reveal, recall.Again, recall.Hard, recall.GotIt, recall.Edit, recall.DontKnow, recall.Record }) button.IsVisible = false;
        PracticeUi.Enable(recall.Reveal, !isTranscribing);
        PracticeUi.Enable(recall.DontKnow, !isTranscribing);
        PracticeUi.Enable(recall.Record, !isTranscribing);
        choice.Previous.IsVisible = active && IsCardMode;
        if (active && IsCardMode)
        {
            card.Record.IsVisible = false;
            card.Submit.IsVisible = false;
            card.End.IsVisible = false;
            choice.Submit.IsVisible = true;
            PracticeUi.Enable(choice.Submit, At(recallDrafts, viewingIndex, "").Length > 0);
            PracticeUi.SetTitle(choice.Submit, viewingIndex == sessionQuestions.Count - 1 ? "Finish" : "Next");
            PracticeUi.Enable(choice.Previous, viewingIndex > 0);
            PracticeUi.SetTitle(View.EndHeader, awaitingCompletion ? "Finish" : "Exit");
            View.Progress.Completed = sessionAnswers.Count;
            View.Progress.CurrentIndex = viewingIndex;
            FinishControls();
            return;
        }
        if (awaitingCompletion)
        {
            card.Record.IsVisible = false;
            card.Submit.IsVisible = false;
            card.End.IsVisible = false;
            PracticeUi.SetTitle(View.EndHeader, "Finish");
            FinishControls();
            return;
        }
        PracticeUi.SetTitle(View.EndHeader, "Exit");
        if (active && IsRecallMode)
        {
            if (reviewing is null)
            {
                recall.Reveal.IsVisible = true;
                recall.DontKnow.IsVisible = true;
                recall.Record.IsVisible = true;
                if (!isRecording && !recordingPermissionPending) PracticeUi.SetTitle(recall.Record, IdleRecallRecordTitle);
            }
            else
            {
                recall.Again.IsVisible = true;
                recall.Hard.IsVisible = true;
                recall.GotIt.IsVisible = true;
                recall.Edit.IsVisible = true;
                var repairing = At(recallRepairing, questionIndex, false);
                PracticeUi.Enable(recall.Again, !repairing);
                PracticeUi.Enable(recall.Hard, !repairing);
                PracticeUi.Enable(recall.GotIt, !repairing);
            }
        }
        FinishControls();
    }

    /// The cards place their buttons by which of them are showing.
    private void FinishControls()
    {
        View.UndoAvailable = lastRatingUndo is not null;
        View.Undo.IsVisible = inSession && lastRatingUndo is not null;
        View.QuestionCard.Relayout();
        View.RecallCard.Relayout();
        View.ChoiceCard.Relayout();
    }

    private string IdleRecallRecordTitle => selectedMode == PracticeRunMode.Rehearse ? "Start speaking" : "Answer aloud";

    private void PresentCurrentQuestion()
    {
        if (recordingPermissionPending) StopRecording();
        usedHelp = false;
        View.QuestionCard.AnswerView.Text = "";
        if (questionIndex >= sessionQuestions.Count)
        {
            FinishRun();
            return;
        }
        RestoreLearnPhase(questionIndex);
        viewingIndex = questionIndex;
        DisplayQuestion(viewingIndex);
        SetStatus(SessionPositionText(sessionQuestions[questionIndex]));
        ShowProgress();
        UpdateSessionControls();
        Relayout();
        Dispatcher.UIThread.Post(FocusResponseIfAppropriate);
    }

    private void RestoreLearnPhase(int index)
    {
        if (!IsRecallMode || index < 0 || index >= sessionQuestions.Count || index >= recallDrafts.Count || !At(recallRevealed, index, false))
        {
            reviewing = null;
            return;
        }
        var response = At(recallResponses, index, null)?.Current ?? recallDrafts[index];
        reviewing = Practice.RecallReview(sessionQuestions[index], response, At(recallCoverageOverrides, index, new List<PracticeCoverageOverride>()));
    }

    private string SessionPositionText(PracticeQuestion question)
    {
        string position;
        if (questionIndex < baseQuestionCount)
        {
            position = $"Question {questionIndex + 1} of {baseQuestionCount}";
        }
        else
        {
            var retryTotal = Math.Max(1, sessionQuestions.Count - baseQuestionCount);
            position = $"Review {questionIndex - baseQuestionCount + 1} of {retryTotal}";
        }
        var group = GroupTitle(question);
        var topic = question.TopicTitle.Trim();
        return topic.Length > 0 && !string.Equals(topic, group, StringComparison.CurrentCultureIgnoreCase)
            ? $"{position} · {group} · {topic}"
            : $"{position} · {group}";
    }

    private void DisplayQuestion(int index)
    {
        if (index < 0 || index >= sessionQuestions.Count) return;
        if (IsCardMode && inSession)
        {
            DisplayCard(index);
            return;
        }
        View.ChoiceCard.IsVisible = false;
        var question = sessionQuestions[index];
        var topic = question.TopicTitle.Length == 0 ? sessionPack?.Title ?? "Practice" : question.TopicTitle;
        var editable = inSession && index == questionIndex && !awaitingCompletion;
        var card = View.QuestionCard;
        var recall = View.RecallCard;

        if (IsRecallMode)
        {
            card.IsVisible = false;
            recall.IsVisible = true;
            var response = At(recallDrafts, index, "");
            if (reviewing is not { } review)
            {
                recall.ShowPrompt(question.Text, response, At(recallConfidences, index, null), showsConfidencePicker: true);
            }
            else
            {
                UpdateRecallRatingButtonTitles(question);
                recall.ShowReview(
                    question.Text, At(recallResponses, index, null)?.Current ?? response, review,
                    coverageIsEstimated: true, allowsManualCoverage: true);
                if (At(recallRepairing, index, false))
                {
                    var savedIndex = At(recallRepairIdeaIndices, index, null);
                    int? repairIndex;
                    if (savedIndex is { } saved)
                    {
                        repairIndex = saved >= 0 && saved < review.KeyIdeas.Count && saved < review.Covered.Count && !review.Covered[saved] ? saved : null;
                    }
                    else
                    {
                        // Older snapshots did not record a target; recover them once.
                        var firstMissed = review.Covered.ToList().IndexOf(false);
                        repairIndex = firstMissed >= 0 ? firstMissed : null;
                    }
                    if (repairIndex is { } target && target < review.KeyIdeas.Count)
                    {
                        Put(recallRepairIdeaIndices, index, target);
                        recall.BeginGapRepair(review.KeyIdeas[target], At(recallRepairDrafts, index, ""));
                    }
                    else
                    {
                        recallRepairing[index] = false;
                        Put(recallRepairIdeaIndices, index, null);
                        Put(recallRepairDrafts, index, "");
                        recall.EndGapRepair();
                    }
                }
                PracticeUi.SetTitle(recall.Edit, EditTitle(review, At(recallRepairing, index, false)));
            }
            UpdateSessionControls();
            return;
        }

        recall.IsVisible = false;
        card.IsVisible = true;
        var answered = index < sessionAnswers.Count;
        var interviewEditable = editable && !answered && !isScoringInterview;
        var existingAnswer = answered
            ? sessionAnswers[index].Answer
            : index < recallDrafts.Count ? recallDrafts[index] : editable ? card.AnswerView.Text ?? "" : "";
        card.ShowQuestion(
            topic, index + 1, sessionQuestions.Count, question.Text,
            Practice.QuestionSourceLine(question, GroupTitle(question)), existingAnswer, interviewEditable);
        PracticeUi.Enable(card.Record, interviewEditable && selectedMode == PracticeRunMode.Interview);
        PracticeUi.Enable(card.Submit, interviewEditable && Practice.ShowsSubmitButton(selectedMode) && !isTranscribing);
    }

    private static string EditTitle(PracticeRecallReview review, bool repairing) =>
        repairing ? "Cancel repair" : review.Covered.Contains(false) ? "Repair one gap" : "Edit response";

    private List<PracticeScoredAnswer> PriorAttempts(PracticeQuestion question)
    {
        var key = Key(question);
        var prior = Practice.RecallAttempts(store.AllRuns()).GetValueOrDefault(key) ?? new List<PracticeScoredAnswer>();
        return prior.Concat(sessionAnswers.Where(answer => Practice.RecallReviewKey(answer.PackId, answer.QuestionId) == key)).ToList();
    }

    private void UpdateRecallRatingButtonTitles(PracticeQuestion question)
    {
        var now = Now();
        var priorAttempts = PriorAttempts(question);
        var confidence = At(recallConfidences, questionIndex, null);
        string Interval(PracticeRecallRating rating)
        {
            var due = Practice.AdaptiveNextReviewDate(rating, priorAttempts, confidence, now, sessionTargetDate);
            var seconds = Math.Max(0, (due - now).TotalSeconds);
            if (seconds < 60 * 60) return $"{Math.Max(1, (int)Math.Ceiling(seconds / 60))} min";
            if (seconds < 36 * 60 * 60) return "tomorrow";
            return $"{Math.Max(2, (int)Math.Ceiling(seconds / (24 * 60 * 60)))} days";
        }
        PracticeUi.SetTitle(View.RecallCard.Again, $"Again · {Interval(PracticeRecallRating.Again)}");
        PracticeUi.SetTitle(View.RecallCard.Hard, $"Hard · {Interval(PracticeRecallRating.Hard)}");
        PracticeUi.SetTitle(View.RecallCard.GotIt, $"Got it · {Interval(PracticeRecallRating.GotIt)}");
    }

    // MARK: - Active Recall

    public void SetCurrentRecallConfidence(PracticeRecallConfidence confidence)
    {
        if (!inSession || !IsRecallMode || questionIndex >= recallConfidences.Count) return;
        recallConfidences[questionIndex] = confidence;
        ScheduleSessionAutosave();
    }

    private void SetCoverageOverride(int index, bool covered)
    {
        if (!inSession || !IsRecallMode || questionIndex >= recallCoverageOverrides.Count || reviewing is null) return;
        var overrides = recallCoverageOverrides[questionIndex].Where(existing => existing.IdeaIndex != index).ToList();
        overrides.Add(new PracticeCoverageOverride(index, covered));
        recallCoverageOverrides[questionIndex] = overrides;

        var clearedActiveRepair = covered && At(recallRepairing, questionIndex, false) && At(recallRepairIdeaIndices, questionIndex, null) == index;
        if (clearedActiveRepair)
        {
            recallRepairing[questionIndex] = false;
            recallRepairIdeaIndices[questionIndex] = null;
            Put(recallRepairDrafts, questionIndex, "");
            View.RecallCard.EndGapRepair();
        }

        reviewing = Practice.RecallReview(sessionQuestions[questionIndex], At(recallDrafts, questionIndex, ""), overrides);
        DisplayQuestion(questionIndex);
        if (clearedActiveRepair) SetStatus("Repair cleared because that idea is now covered.");
        ScheduleSessionAutosave();
    }

    public void BeginGapRepair()
    {
        if (!inSession || !IsRecallMode || reviewing is not { } review) return;
        if (At(recallRepairing, questionIndex, false))
        {
            CancelGapRepair();
            return;
        }
        var missedIndex = review.Covered.ToList().IndexOf(false);
        if (missedIndex < 0 || missedIndex >= review.KeyIdeas.Count)
        {
            EditRecallResponse();
            return;
        }
        Put(recallRepairing, questionIndex, true);
        Put(recallRepairIdeaIndices, questionIndex, missedIndex);
        View.RecallCard.BeginGapRepair(review.KeyIdeas[missedIndex], At(recallRepairDrafts, questionIndex, ""));
        SetStatus("Repair one missed idea in your own words.");
        PracticeUi.SetTitle(View.RecallCard.Edit, "Cancel repair");
        UpdateSessionControls();
        SaveSessionSnapshot();
        Dispatcher.UIThread.Post(FocusResponseIfAppropriate);
    }

    private void CancelGapRepair()
    {
        if (questionIndex >= recallRepairing.Count) return;
        recallRepairing[questionIndex] = false;
        Put(recallRepairIdeaIndices, questionIndex, null);
        Put(recallRepairDrafts, questionIndex, "");
        View.RecallCard.EndGapRepair();
        if (reviewing is { } review) PracticeUi.SetTitle(View.RecallCard.Edit, EditTitle(review, repairing: false));
        SetStatus("Repair cancelled. Your original response is unchanged.");
        UpdateSessionControls();
        FocusResponseIfAppropriate();
        SaveSessionSnapshot();
    }

    private void UpdateGapRepairDraft(string draft)
    {
        if (!inSession || !IsRecallMode || questionIndex >= recallRepairDrafts.Count) return;
        // The repair box also reports the text the app put there; only the person's edits are drafts.
        if (!At(recallRepairing, questionIndex, false) || recallRepairDrafts[questionIndex] == draft) return;
        recallRepairDrafts[questionIndex] = draft;
        ScheduleSessionAutosave();
    }

    public void CompleteGapRepair(string revision)
    {
        var cleaned = revision.Trim();
        if (cleaned.Length == 0 || !inSession || !IsRecallMode || questionIndex >= sessionQuestions.Count)
        {
            View.RecallCard.SetGapRepairValidationMessage("Add one sentence in your own words.");
            return;
        }
        var existing = At(recallResponses, questionIndex, null);
        var initial = existing?.Initial ?? recallDrafts[questionIndex];
        var current = existing?.Current ?? initial;
        var revised = current.Length == 0 ? cleaned : $"{current}\n{cleaned}";
        Put(recallResponses, questionIndex, new PracticeRecallResponse(initial, revised));
        Put(recallDrafts, questionIndex, revised);
        Put(recallCoverageOverrides, questionIndex, new List<PracticeCoverageOverride>());
        Put(recallUsedHelp, questionIndex, true);
        Put(recallRevealed, questionIndex, false);
        Put(recallRepairing, questionIndex, false);
        Put(recallRepairIdeaIndices, questionIndex, null);
        Put(recallRepairDrafts, questionIndex, "");
        View.RecallCard.EndGapRepair();
        PresentRecallReview(revised);
    }

    public void RevealRecallAnswer()
    {
        if (!inSession || questionIndex >= sessionQuestions.Count || !IsRecallMode) return;
        if (isRecording)
        {
            StopRecording();
            TranscribeRecording();
            return;
        }
        var response = (View.RecallCard.ResponseView.Text ?? "").Trim();
        if (response.Length == 0)
        {
            SetStatus("Write what you remember before revealing the key ideas.");
            return;
        }
        PresentRecallReview(response);
    }

    public void RevealRecallWithoutAnswer()
    {
        if (!inSession || questionIndex >= sessionQuestions.Count || !IsRecallMode) return;
        if (isRecording)
        {
            StopRecording();
            TranscribeRecording();
            return;
        }
        if (isTranscribing) return;
        Put(recallConfidences, questionIndex, PracticeRecallConfidence.Unsure);
        Put(recallCoverageOverrides, questionIndex, new List<PracticeCoverageOverride>());
        PresentRecallReview("I don’t know yet.");
    }

    private void PresentRecallReview(string response)
    {
        if (recordingPermissionPending) StopRecording();
        Put(recallDrafts, questionIndex, response);
        if (questionIndex < recallResponses.Count)
        {
            recallResponses[questionIndex] = recallResponses[questionIndex] is { } existing
                ? new PracticeRecallResponse(existing.Initial, response == existing.Initial ? null : response)
                : new PracticeRecallResponse(response, null);
        }
        Put(recallRevealed, questionIndex, true);
        var review = Practice.RecallReview(
            sessionQuestions[questionIndex], response, At(recallCoverageOverrides, questionIndex, new List<PracticeCoverageOverride>()));
        reviewing = review;
        DisplayQuestion(questionIndex);
        var confidence = At(recallConfidences, questionIndex, null);
        if (confidence == PracticeRecallConfidence.Very && review.CoveredCount < review.KeyIdeas.Count)
        {
            SetStatus("Confident gap · repair the first missed idea before rating.");
        }
        else if (confidence == PracticeRecallConfidence.Unsure && review.Score >= 0.8)
        {
            SetStatus("Stronger than expected · you covered most key ideas.");
        }
        else
        {
            SetStatus(SessionPositionText(sessionQuestions[questionIndex]));
        }
        UpdateSessionControls();
        Relayout();
        Dispatcher.UIThread.Post(FocusResponseIfAppropriate);
        SaveSessionSnapshot();
    }

    private void EditRecallResponse()
    {
        if (!inSession || !IsRecallMode || questionIndex >= sessionQuestions.Count || reviewing is null) return;
        Put(recallUsedHelp, questionIndex, true);
        Put(recallRevealed, questionIndex, false);
        Put(recallRepairing, questionIndex, false);
        Put(recallRepairDrafts, questionIndex, "");
        Put(recallRepairIdeaIndices, questionIndex, null);
        Put(recallCoverageOverrides, questionIndex, new List<PracticeCoverageOverride>());
        reviewing = null;
        DisplayQuestion(questionIndex);
        SetStatus("Editing after reveal · your original answer is preserved.");
        UpdateSessionControls();
        Relayout();
        Dispatcher.UIThread.Post(FocusResponseIfAppropriate);
        SaveSessionSnapshot();
    }

    public void RateRecall(PracticeRecallRating rating)
    {
        if (!inSession || !IsRecallMode || questionIndex >= sessionQuestions.Count || awaitingCompletion || reviewing is not { } review) return;
        var question = sessionQuestions[questionIndex];
        var response = At(recallDrafts, questionIndex, "");
        var helped = At(recallUsedHelp, questionIndex, false);
        var rawScore = review.KeyIdeas.IsEmpty ? rating.FallbackScore() : review.Score;
        var reviewedAt = Now();
        var responseRecord = At(recallResponses, questionIndex, null) ?? new PracticeRecallResponse(response, null);
        var confidence = At(recallConfidences, questionIndex, null);
        var overrides = At(recallCoverageOverrides, questionIndex, new List<PracticeCoverageOverride>());
        var strengths = new List<string>();
        var gaps = new List<string>();
        for (var index = 0; index < review.KeyIdeas.Count && index < review.Covered.Count; index++)
        {
            (review.Covered[index] ? strengths : gaps).Add(review.KeyIdeas[index]);
        }
        var feedback = review.KeyIdeas.IsEmpty
            ? $"Self-rated {rating.Title()}"
            : $"Covered {review.CoveredCount} of {review.KeyIdeas.Count} key ideas · {rating.Title()}";
        var nextReviewAt = Practice.AdaptiveNextReviewDate(rating, PriorAttempts(question), confidence, reviewedAt, sessionTargetDate);

        lastRatingUndo = MakeSessionQuestionState(questionIndex) is { } previousState
            ? new PracticeRatingUndoMetadata
            {
                Question = previousState.Reference,
                QuestionIndex = questionIndex,
                AnswerCountBeforeRating = sessionAnswers.Count,
                QuestionCountBeforeRating = sessionQuestions.Count,
                PreviousQuestionState = previousState,
                RatedAt = reviewedAt,
                RequeuedQuestionKeysBeforeRating = recallRequeuedQuestionIDs.OrderBy(key => key, StringComparer.Ordinal).ToList(),
            }
            : null;

        sessionAnswers.Add(new PracticeScoredAnswer
        {
            QuestionId = question.Id,
            PackId = question.PackId,
            Question = question.Text,
            Answer = response,
            UsedHelp = helped,
            Mark = helped ? PracticeHelpMark.Yellow : PracticeHelpMark.None,
            RawScore = rawScore,
            FinalScore = rawScore,
            Feedback = feedback,
            Strengths = strengths,
            Gaps = gaps,
            RecallRating = rating,
            NextReviewAt = nextReviewAt,
            RecallResponse = responseRecord,
            RecallConfidence = confidence,
            CoverageOverrides = overrides.ToList(),
            ReviewedAt = reviewedAt,
        });

        if (rating == PracticeRecallRating.Again && recallRequeuedQuestionIDs.Add(Key(question)))
        {
            sessionQuestions.Add(question);
            recallDrafts.Add("");
            recallUsedHelp.Add(false);
            recallResponses.Add(null);
            recallConfidences.Add(null);
            recallCoverageOverrides.Add(new List<PracticeCoverageOverride>());
            recallRevealed.Add(false);
            recallRepairDrafts.Add("");
            recallRepairing.Add(false);
            recallRepairIdeaIndices.Add(null);
        }

        if (questionIndex + 1 >= sessionQuestions.Count)
        {
            awaitingCompletion = true;
            UpdateSessionControls();
            SetStatus("Rating saved · Undo it or finish the run.");
            SaveSessionSnapshot();
            Relayout();
            Dispatcher.UIThread.Post(FocusResponseIfAppropriate);
        }
        else
        {
            questionIndex += 1;
            PresentCurrentQuestion();
            SaveSessionSnapshot();
        }
    }

    // MARK: - Interview answers

    public void SubmitAnswer()
    {
        if (!inSession || questionIndex >= sessionQuestions.Count) return;
        if (IsCardMode)
        {
            AdvanceCardQuestion();
            return;
        }
        if (IsRecallMode)
        {
            RevealRecallAnswer();
            return;
        }
        var card = View.QuestionCard;
        if (!Practice.ShowsSubmitButton(selectedMode) || !card.Submit.IsEnabled) return;
        if (recordingPermissionPending) StopRecording();
        if (isRecording)
        {
            StopRecording();
            TranscribeRecording(() =>
            {
                if (CanPresentPracticeUI?.Invoke() == true) SubmitAnswer();
                else SaveSessionSnapshot();
            });
            return;
        }
        var answer = (card.AnswerView.Text ?? "").Trim();
        if (answer.Length == 0)
        {
            SetStatus("Record or type an answer first.");
            return;
        }
        SetStatus("Scoring…");
        isScoringInterview = true;
        PracticeUi.Enable(card.Submit, false);
        PracticeUi.Enable(card.Record, false);
        card.AnswerView.IsReadOnly = true;
        _ = ScoreAnswerAsync(sessionQuestions[questionIndex], answer, usedHelp, sessionSnapshotID, questionIndex);
    }

    private async Task ScoreAnswerAsync(PracticeQuestion question, string answer, bool helped, string expectedSnapshotID, int expectedQuestionIndex)
    {
        var judged = await JudgeAsync(question.Text, answer, question.Rubric).ConfigureAwait(false);
        var penalty = Practice.ApplyHelpPenalty(judged.Score, helped);
        var scored = new PracticeScoredAnswer
        {
            QuestionId = question.Id,
            PackId = question.PackId,
            Question = question.Text,
            Answer = answer,
            UsedHelp = penalty.UsedHelp,
            Mark = penalty.Mark,
            RawScore = penalty.RawScore,
            FinalScore = penalty.FinalScore,
            Feedback = judged.Feedback,
            Strengths = judged.Strengths.ToList(),
            Gaps = judged.Gaps.ToList(),
        };
        Dispatcher.UIThread.Post(() =>
        {
            if (!inSession || sessionSnapshotID != expectedSnapshotID || questionIndex != expectedQuestionIndex
                || expectedQuestionIndex >= sessionQuestions.Count || Key(sessionQuestions[expectedQuestionIndex]) != Key(question))
            {
                return;
            }
            sessionAnswers.Add(scored);
            isScoringInterview = false;
            PracticeUi.Enable(View.QuestionCard.Submit, true);
            if (questionIndex + 1 >= sessionQuestions.Count)
            {
                awaitingCompletion = true;
                UpdateSessionControls();
                SetStatus("Answer scored · finish the run when ready.");
                SaveSessionSnapshot();
                Relayout();
                FocusResponseIfAppropriate();
            }
            else
            {
                questionIndex += 1;
                PresentCurrentQuestion();
                SaveSessionSnapshot();
            }
        });
    }

    private async Task<PracticeGroqClient.Judgement> JudgeAsync(string question, string answer, string rubric)
    {
        PracticeGroqClient.Judgement Local(string feedback) =>
            new(Practice.HeuristicRawScore(question, answer), feedback, Array.Empty<string>(), Array.Empty<string>());
        if (MakeClient() is not { } client) return Local("Local score (no Groq key).");
        try
        {
            return await client.JudgeAnswerAsync(question, answer, rubric).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return Local("Local score (judge unavailable).");
        }
    }

    // MARK: - Leaving and finishing

    public async Task EndRunAsync()
    {
        if (!inSession) return;
        if (recordingPermissionPending) StopRecording();
        if (isTranscribing || isScoringInterview)
        {
            SetStatus("Wait for the current Practice result to finish.");
            return;
        }
        if (isRecording)
        {
            StopRecording();
            TranscribeRecording(() =>
            {
                if (CanPresentPracticeUI?.Invoke() == true) _ = EndRunAsync();
                else SaveSessionSnapshot();
            });
            return;
        }
        if (awaitingCompletion)
        {
            FinishRun();
            return;
        }
        if (IsRecallMode) Put(recallDrafts, questionIndex, View.RecallCard.ResponseView.Text ?? "");
        var snapshotSavedBeforePrompt = SaveSessionSnapshot();
        var choice = await shell.AskAsync(new AppQuestion(
            "Leave this Practice session?",
            "Save it to resume later, keep practicing, or discard the unfinished session.",
            new[] { "Save & Exit", "Continue", "Discard" }));
        switch (choice)
        {
            case 0:
                if (snapshotSavedBeforePrompt || SaveSessionSnapshot())
                {
                    LeaveToTimeline?.Invoke();
                }
                else
                {
                    SetStatus("Could not save this Practice session. Keep practicing or choose Discard.");
                    FocusResponseIfAppropriate();
                }
                break;
            case 2:
                ResetIdle("Practice session discarded.");
                break;
            default:
                FocusResponseIfAppropriate();
                break;
        }
    }

    public void FinishRun()
    {
        if (IsCardMode && sessionAnswers.Count != sessionQuestions.Count) return;
        autosave?.Dispose();
        recordingPermissionGeneration += 1;
        transcriptionGeneration += 1;
        isTranscribing = false;
        isScoringInterview = false;
        StopRecording();
        if (sessionPack is not { } pack)
        {
            ResetIdle("No pack.");
            return;
        }
        var run = Practice.MakeRunRecord(pack, sessionStartedAt, Now(), sessionAnswers, selectedMode, sessionTargetDate, sessionSnapshotID);
        if (!store.Append(run))
        {
            SetStatus("Could not save the run. Your resumable session is still available.");
            SaveSessionSnapshot();
            return;
        }
        var clearedSnapshot = store.ClearInProgressSession();
        lastRatingUndo = null;
        awaitingCompletion = false;
        inSession = false;
        View.ChoiceCard.IsVisible = false;
        PracticeUi.Enable(View.Start, true);
        SetSetupEnabled(true);
        View.RecallCard.IsVisible = false;
        View.QuestionCard.IsVisible = true;
        UpdateSessionControls();
        View.QuestionCard.ShowReport(FormatReport(run));
        SetStatus(clearedSnapshot
            ? $"Run saved · {Percent(run.OverallScore)}% overall"
            : $"Run saved · {Percent(run.OverallScore)}% overall · old resume data could not be cleared");
        ReloadHistory();
        Relayout();
        RequestInputFocus?.Invoke(View.QuestionCard.ReportView);
    }

    private void ResetIdle(string status)
    {
        autosave?.Dispose();
        recordingPermissionGeneration += 1;
        transcriptionGeneration += 1;
        isTranscribing = false;
        StopRecording();
        var clearedSnapshot = store.ClearInProgressSession();
        inSession = false;
        View.ChoiceCard.IsVisible = false;
        sessionQuestions = new();
        sessionAnswers = new();
        recallDrafts = new();
        recallUsedHelp = new();
        recallResponses = new();
        recallConfidences = new();
        recallCoverageOverrides = new();
        recallRevealed = new();
        recallRepairDrafts = new();
        recallRepairing = new();
        recallRepairIdeaIndices = new();
        recallRequeuedQuestionIDs = new HashSet<string>(StringComparer.Ordinal);
        baseQuestionCount = 0;
        lastRatingUndo = null;
        awaitingCompletion = false;
        usedHelp = false;
        reviewing = null;
        PracticeUi.Enable(View.Start, true);
        SetSetupEnabled(true);
        View.RecallCard.IsVisible = false;
        View.QuestionCard.IsVisible = true;
        UpdateSessionControls();
        viewingIndex = 0;
        View.QuestionCard.ShowIdle();
        SetStatus(clearedSnapshot ? status : $"{status} Saved resume data could not be cleared.");
        UpdateIdlePlan();
        Relayout();
        RequestInputFocus?.Invoke(View.Start);
    }

    // MARK: - Speaking an answer

    private TextBox CurrentEditor => IsRecallMode ? View.RecallCard.ResponseView : View.QuestionCard.AnswerView;

    public void ToggleRecord()
    {
        if (isTranscribing || isScoringInterview)
        {
            SetStatus(isTranscribing ? "Finishing the current transcription…" : "Finishing the current score…");
            return;
        }
        if (recordingPermissionPending)
        {
            StopRecording();
            SetStatus("Recording cancelled.");
        }
        else if (isRecording)
        {
            StopRecording();
            TranscribeRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        if (!inSession || isTranscribing || isScoringInterview || isRecording || recordingPermissionPending) return;
        if (recorder is null || AcquireMicrophone is null || OwnsMicrophone is not { } owns || ReleaseMicrophone is not { } release)
        {
            SetStatus("Microphone unavailable. Type your answer instead.");
            return;
        }
        if (AcquireMicrophone() is not { } lease)
        {
            SetStatus("Microphone is in use by the interview. Switch it to system audio or type your answer.");
            return;
        }
        microphoneLease = lease;
        recordingPermissionPending = true;
        recorder.Discard();
        recordingPermissionGeneration += 1;
        transcriptionGeneration += 1;
        var expectedPermissionGeneration = recordingPermissionGeneration;
        var expectedSnapshotID = sessionSnapshotID;
        var expectedQuestionIndex = questionIndex;
        PracticeUi.SetTitle(View.QuestionCard.Record, "Cancel");
        PracticeUi.SetTitle(View.RecallCard.Record, "Cancel recording");
        SetStatus("Waiting for microphone permission…");
        _ = AwaitMicrophoneAsync(recorder, lease, owns, release, expectedPermissionGeneration, expectedSnapshotID, expectedQuestionIndex);
    }

    private async Task AwaitMicrophoneAsync(
        IPracticeRecorder activeRecorder, MicrophoneLease lease, Func<MicrophoneLease, bool> owns, Action<MicrophoneLease> release,
        int expectedPermissionGeneration, string expectedSnapshotID, int expectedQuestionIndex)
    {
        // A late completion owns only its original reservation.
        bool StillWanted()
        {
            if (inSession && !isTranscribing && !isScoringInterview
                && recordingPermissionGeneration == expectedPermissionGeneration
                && sessionSnapshotID == expectedSnapshotID && questionIndex == expectedQuestionIndex
                && microphoneLease == lease && owns(lease))
            {
                return true;
            }
            if (microphoneLease == lease) StopRecording();
            else release(lease);
            return false;
        }

        bool granted;
        try
        {
            granted = await activeRecorder.RequestAccessAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            granted = false;
        }
        var ready = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!StillWanted()) return false;
            if (granted) return true;
            StopRecording();
            SetStatus("Microphone permission needed, or type your answer.");
            RequestInputFocus?.Invoke(CurrentEditor);
            return false;
        });
        if (!ready) return;

        // Opening the device takes a moment on Windows; until it is open a second press still cancels.
        bool started;
        try
        {
            started = await activeRecorder.StartAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            started = false;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (!StillWanted())
            {
                activeRecorder.Stop();
                activeRecorder.Discard();
                return;
            }
            recordingPermissionPending = false;
            if (!started)
            {
                StopRecording();
                SetStatus("Could not record. Type your answer instead.");
                RequestInputFocus?.Invoke(CurrentEditor);
                return;
            }
            isRecording = true;
            PracticeUi.SetTitle(View.QuestionCard.Record, "Stop");
            PracticeUi.SetTitle(View.RecallCard.Record, "Stop recording");
            SetStatus("Recording answer…");
        });
    }

    private void StopRecording()
    {
        recordingPermissionGeneration += 1;
        recordingPermissionPending = false;
        recorder?.Stop();
        isRecording = false;
        if (microphoneLease is { } lease)
        {
            microphoneLease = null;
            ReleaseMicrophone?.Invoke(lease);
        }
        PracticeUi.SetTitle(View.QuestionCard.Record, "Record");
        PracticeUi.SetTitle(View.RecallCard.Record, IdleRecallRecordTitle);
    }

    private void TranscribeRecording(Action? completion = null)
    {
        if (recorder?.Recording is not { Length: > 0 } data)
        {
            isTranscribing = false;
            SetStatus("No audio captured. Type your answer.");
            RequestInputFocus?.Invoke(CurrentEditor);
            completion?.Invoke();
            return;
        }
        transcriptionGeneration += 1;
        var expectedTranscriptionGeneration = transcriptionGeneration;
        isTranscribing = true;
        UpdateSessionControls();
        SetStatus("Transcribing…");
        var editorTextAtStart = CurrentEditor.Text ?? "";
        _ = TranscribeAsync(data, expectedTranscriptionGeneration, sessionSnapshotID, questionIndex, editorTextAtStart, completion);
    }

    private async Task TranscribeAsync(
        byte[] data, int expectedTranscriptionGeneration, string expectedSnapshotID, int expectedQuestionIndex, string editorTextAtStart, Action? completion)
    {
        var captured = "";
        if (MakeClient() is { } client)
        {
            try
            {
                captured = await client.TranscribeAsync(data).ConfigureAwait(false);
            }
            catch (Exception)
            {
                captured = "";
            }
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (!inSession || transcriptionGeneration != expectedTranscriptionGeneration || sessionSnapshotID != expectedSnapshotID
                || questionIndex != expectedQuestionIndex || expectedQuestionIndex >= sessionQuestions.Count)
            {
                return;
            }
            isTranscribing = false;
            UpdateSessionControls();
            var currentEditorText = CurrentEditor.Text ?? "";
            if (currentEditorText != editorTextAtStart && currentEditorText.Trim().Length > 0)
            {
                SetStatus("Transcript finished; your newer typed edits were kept.");
                ScheduleSessionAutosave();
                completion?.Invoke();
                return;
            }
            if (captured.Length == 0)
            {
                SetStatus("Transcription unavailable. Type your answer.");
                RequestInputFocus?.Invoke(CurrentEditor);
            }
            else
            {
                Put(recallDrafts, questionIndex, captured);
                CurrentEditor.Text = captured;
                SetStatus(IsRecallMode ? "Answer captured. Reveal the key ideas when ready." : "Answer captured. Submit when ready.");
                ScheduleSessionAutosave();
                FocusResponseIfAppropriate();
            }
            completion?.Invoke();
        });
    }
}
