using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.AppServices;

/// Answer prompts, the answer streams, and live coding.
public partial class VoiceInterviewProcessor
{
    // Fast Quality Answer

    /// The last few exchanges for an answer prompt, or null at the start of a session.
    private static string? RecentConversationForPrompt(ConversationContext context)
    {
        var recent = context.GetContextForLLM();
        return recent == "No previous conversation." ? null : recent;
    }

    internal static string DirectHaikuAnswerPrompt(
        string text,
        string topic,
        bool topicInherited,
        string userBackground,
        ConversationContext context,
        string? pinnedSolution)
    {
        var normalized = NormalizedQuestionText(text);
        var isVagueFollowUp = IsVagueFollowUpPrompt(normalized, TechnicalQuestionTokens(normalized));

        var backgroundContext = Briefing.ResumeGroundingInstruction(userBackground, text, topic, context.GetContextForLLM());

        // The fast paths used to see only the bare question, so "those methods" or "these tools" were
        // answered blind. The recent conversation is always included and only resolves references; the
        // topic line is what a stale subject used to leak through, and that is handled separately.
        var conversationBlock = AnswerPromptBlocks.RecentConversation(RecentConversationForPrompt(context), isVagueFollowUp);
        var pinnedContext = AnswerPromptBlocks.PinnedCode(pinnedSolution);
        var topicBlock = AnswerPromptBlocks.TopicBlock(topic, topicInherited);
        var topicGuidance = topicBlock.AllowsTopicSpecificShape ? TopicSpecificAnswerGuidance(topic) : "";
        var topicLine = topicBlock.Line.Length == 0 ? "" : $"\n{topicBlock.Line}";

        var settings = AppSettings.Shared;
        var languageInstruction = settings.LlmLanguageInstruction;
        return $"""
            You are helping a candidate answer a live technical interview question.
            Start immediately with useful answer text. No intro, no labels, no clarification request.

            {settings.InterviewContext}
            {settings.AnswerStyleInstruction}
            {backgroundContext}{conversationBlock}{pinnedContext}
            QUESTION: "{text}"{topicLine}
            {topicGuidance}

            {topicBlock.GarbleNote}
            Output 3-5 cue-card bullets only. Every line starts with "- ".
            Keep each bullet short enough to read while speaking.
            Plain text only. No markdown, no **bold**, no headings.
            For acronyms like SOLID, one bullet per letter as "S - Single Responsibility: ...".

            {languageInstruction}
            """;
    }

    internal static string TopicSpecificAnswerGuidance(string topic)
    {
        var topicLower = topic.Lowercased();
        if (topicLower == "solid")
        {
            return """

                SOLID ANSWER SHAPE:
                One bullet per letter. Plain text. Never write **S**ingle or S**.
                - S - Single Responsibility: one class, one reason to change
                - O - Open/Closed: extend without modifying existing code
                - L - Liskov Substitution: subtypes must replace their base type
                - I - Interface Segregation: many small interfaces, not one fat one
                - D - Dependency Inversion: depend on abstractions, not concretions
                """;
        }
        if (topicLower is not ("rag" or "cag" or "ragcag")) return "";

        return """

            RAG/CAG ANSWER SHAPE:
            - First bullet: "RAG (Retrieval-Augmented Generation) means..."
            - Explain that docs, pages, PDFs, images, or video/audio transcripts are chunked and embedded.
            - Mention those embeddings are stored in a vector DB and searched semantically.
            - End with the LLM using retrieved chunks as context, plus one practical trade-off.
            - Avoid abstract-only wording like "combines a vector store with generation".
            """;
    }

    // Live coding

    private int CurrentTurnGeneration()
    {
        lock (turnLock) return turnGeneration;
    }

    /// A change asked for from the code panel ("Another way", "Add tests") instead of by voice. It takes
    /// the same path as the spoken request and opens its own card.
    public void RequestCodeChange(string instruction)
    {
        if (anthropicClient is not { } haiku || Delegate?.ConversationContext is not { } context) return;
        lock (processingStateLock)
        {
            if (codingSession.CurrentCode.Length == 0) return;
        }
        var turnId = Guid.NewGuid();
        var sequence = ReserveTurnSequence();
        var generation = CurrentTurnGeneration();
        StartTurnTask(turnId, async ct =>
        {
            await HandleCodingTurnAsync(
                CodingTurnDecision.ChangeCode(instruction), instruction, CaptureDevice.Microphone, haiku, context,
                turnId, sequence, generation, DateTime.UtcNow, ct).ConfigureAwait(false);
            await HideLoading(turnId).ConfigureAwait(false);
        });
    }

    /// Asks for the answer to a question again on the card that failed, so the card fills instead of a
    /// second one opening. A card that came from a coding request asks for its code again instead.
    public void RetryAnswer(Guid turnId, string question, string topic, int sequence)
    {
        Guid? codingCard;
        lock (processingStateLock) codingCard = lastCodingRequest?.CardTurnId;
        if (codingCard == turnId)
        {
            RetryLastCodingRequest();
            return;
        }
        if (anthropicClient is not { } haiku || Delegate?.ConversationContext is not { } context) return;
        var taskId = Guid.NewGuid();
        var generation = CurrentTurnGeneration();
        StartTurnTask(taskId, async ct =>
        {
            await OnMain(() => Delegate?.ProcessorShowLoading("💭 Generating answer...", LoadingColor.AppleGreen, turnId)).ConfigureAwait(false);
            var background = await mainThread.RunAsync(() => Delegate?.UserBackground ?? "").ConfigureAwait(false);
            var pinned = await mainThread.RunAsync(() => Delegate?.PinnedSolution).ConfigureAwait(false);
            var answered = await StreamDirectHaikuAnswerAsync(
                question, topic, false, CaptureDevice.Microphone, haiku, background, context, pinned, turnId, sequence,
                generation, DateTime.UtcNow, true, ct).ConfigureAwait(false);
            await OnMain(() =>
            {
                if (!answered) Delegate?.ProcessorDidReceiveAnswerChunk("Error: No answer text received", turnId);
                Delegate?.ProcessorHideLoading(turnId);
            }).ConfigureAwait(false);
        });
    }

    /// Runs the last coding request again on the same card, for code that stopped early.
    public void RetryLastCodingRequest()
    {
        if (anthropicClient is not { } haiku || Delegate?.ConversationContext is not { } context) return;
        CodingRequest? request;
        lock (processingStateLock) request = lastCodingRequest;
        if (request is null) return;
        var turnId = request.CardTurnId;
        var taskId = Guid.NewGuid();
        var generation = CurrentTurnGeneration();
        StartTurnTask(taskId, async ct =>
        {
            await StreamCodingInterviewStepsAsync(
                request.Task, request.Text, false, request.Change, request.CardTurnId, CaptureDevice.Microphone, haiku, context,
                turnId, request.TurnSequence, generation, DateTime.UtcNow, ct).ConfigureAwait(false);
            await HideLoading(turnId).ConfigureAwait(false);
        });
    }

    /// Returns true when the turn was consumed by the coding flow and must not also open a cue card.
    private async Task<bool> HandleCodingTurnAsync(
        CodingTurnDecision decision,
        string text,
        CaptureDevice source,
        AnthropicClient haiku,
        ConversationContext context,
        Guid turnId,
        int turnSequence,
        int turnGeneration,
        DateTime questionEndTime,
        CancellationToken ct)
    {
        switch (decision.Kind)
        {
            case CodingTurnKind.ArmSession:
                lock (processingStateLock) codingSession.Arm(DateTime.UtcNow);
                // "Let's start a new Python file" is logistics. A long turn may still hold a real question.
                return text.SplitOmittingEmpty(' ').Count <= 20;
            case CodingTurnKind.NeedsScreenContext:
                return await ShowScreenContextHintAsync(text, source, context, turnId, turnSequence, turnGeneration, questionEndTime).ConfigureAwait(false);
            case CodingTurnKind.NewTask:
                return await StreamCodingInterviewStepsAsync(
                    decision.Text, text, false, null, null, source, haiku, context,
                    turnId, turnSequence, turnGeneration, questionEndTime, ct).ConfigureAwait(false);
            case CodingTurnKind.RefineTask:
                return await StreamCodingInterviewStepsAsync(
                    decision.Text, text, true, null, null, source, haiku, context,
                    turnId, turnSequence, turnGeneration, questionEndTime, ct).ConfigureAwait(false);
            default:
                // "Write it in TypeScript", "another way": the model gets the code on screen plus the request,
                // and the answer opens its own card because the interviewer asked something new.
                string original;
                lock (processingStateLock) original = codingSession.Task;
                return await StreamCodingInterviewStepsAsync(
                    original, text, false, decision.Text, null, source, haiku, context,
                    turnId, turnSequence, turnGeneration, questionEndTime, ct).ConfigureAwait(false);
        }
    }

    internal static readonly string ScreenContextHint =
        "- The task is on the interviewer's screen, not in what they said.\n"
        + $"- Capture it with {PlatformCopy.CaptureShortcut}, then press {PlatformCopy.AnalyzeShortcut} and the code appears in the code panel.\n"
        + "- Meanwhile confirm the language, the input, and the expected output out loud.";

    /// "Write this code" with the task only on the interviewer's screen: there is nothing to generate
    /// from, so the card says how to get the task in instead of a model inventing one.
    private async Task<bool> ShowScreenContextHintAsync(
        string text,
        CaptureDevice source,
        ConversationContext context,
        Guid turnId,
        int turnSequence,
        int turnGeneration,
        DateTime questionEndTime)
    {
        var hint = ScreenContextHint;
        lock (processingStateLock)
        {
            if (!IsCurrentTurn(turnGeneration)) return true;
            context.AddUtterance(text, "coding", isQuestion: true, sequence: turnSequence);
            lastAnswerTime = DateTime.UtcNow;
        }
        var latencyMs = MillisecondsSince(questionEndTime);
        await OnMain(() =>
        {
            Delegate?.ProcessorDidReceiveQuestion(text, "coding", MessageType.Answer, source, turnId, turnSequence);
            Delegate?.ProcessorDidStartStreaming(MessageType.Answer, "coding", latencyMs, turnId, turnSequence);
            Delegate?.ProcessorDidReceiveAnswerChunk(hint, turnId);
            Delegate?.ProcessorDidFinishAnswer(hint, latencyMs, turnId);
        }).ConfigureAwait(false);
        return true;
    }

    /// Streams the code for a spoken task. Talking points go to the answer card on the left and the
    /// typed steps go to the code panel, so both keep updating while questions continue.
    /// A refinement updates the card and panel of the exercise in progress instead of opening another.
    private async Task<bool> StreamCodingInterviewStepsAsync(
        string task,
        string text,
        bool refining,
        string? change,
        Guid? reusingCard,
        CaptureDevice source,
        AnthropicClient haiku,
        ConversationContext context,
        Guid turnId,
        int turnSequence,
        int turnGeneration,
        DateTime questionEndTime,
        CancellationToken ct)
    {
        var language = AppSettings.Shared.ProgrammingLanguage.CodeBlockLang();
        string codeOnScreen;
        lock (processingStateLock) codeOnScreen = codingSession.CurrentCode;
        // Only a change or a dictation refinement builds on the code that is there. A new task starts clean.
        var buildsOnCode = (refining || change is not null) && codeOnScreen.Length > 0;
        var prompt = CodingSteps.Prompt(language, task, spoken: true, currentCode: buildsOnCode ? codeOnScreen : null, changeRequest: change);
        DebugLog(LogCategory.Answer, $"💻 CODING: generating (refining={Flag(refining)}, change={Flag(change is not null)}, codeOnScreen={codeOnScreen.Length} chars) for '{task.Suffix(80)}'");
        var state = new AnswerStreamState();
        var timing = new CodingStreamTiming();

        Guid cardTurnId;
        bool opensCard;
        int generationToken;
        lock (processingStateLock)
        {
            codingGenerationToken++;
            generationToken = codingGenerationToken;
            if (reusingCard is { } reused)
            {
                cardTurnId = reused;
                opensCard = false;
            }
            else if (refining && codingSession.CardTurnId is { } existing)
            {
                cardTurnId = existing;
                opensCard = false;
            }
            else
            {
                cardTurnId = turnId;
                opensCard = true;
            }
            if (opensCard)
            {
                lastCodingRequest = new CodingRequest(task, text, change, cardTurnId, turnSequence);
            }
        }

        bool IsSuperseded()
        {
            lock (processingStateLock) return generationToken != codingGenerationToken;
        }

        await OnMain(() => Delegate?.ProcessorShowLoading("💻 Writing code...", LoadingColor.ApplePurple, turnId)).ConfigureAwait(false);

        var result = await haiku.StreamTextMessageAsync(
            prompt,
            chunk =>
            {
                if (!IsCurrentTurn(turnGeneration) || state.AppendVisibleChunk(chunk) is not { } update) return;
                lock (processingStateLock)
                {
                    if (generationToken != codingGenerationToken) return;
                    if (update.IsFirst)
                    {
                        var now = DateTime.UtcNow;
                        if (opensCard)
                        {
                            context.AddUtterance(text, "coding", isQuestion: true, sequence: turnSequence);
                            if (change is not null)
                            {
                                codingSession.ApplyChange(text, cardTurnId, now);
                            }
                            else
                            {
                                codingSession.Begin(task, cardTurnId, now);
                            }
                        }
                        else if (reusingCard is not null)
                        {
                            codingSession.Touch(now);
                        }
                        else
                        {
                            codingSession.Refine(task, now);
                        }
                        lastAnswerTime = DateTime.UtcNow;
                    }
                }
                var snapshot = update.Snapshot;
                var cardStartMs = MillisecondsSince(questionEndTime);
                mainThread.Post(() =>
                {
                    if (!IsCurrentTurn(turnGeneration)) return;
                    if (IsSuperseded()) return;
                    if (update.IsFirst)
                    {
                        if (opensCard)
                        {
                            Delegate?.ProcessorDidReceiveQuestion(text, "coding", MessageType.Answer, source, turnId, turnSequence);
                            Delegate?.ProcessorDidStartStreaming(MessageType.Answer, "coding", cardStartMs, turnId, turnSequence);
                        }
                        Delegate?.ProcessorDidStartCodingTask(new CodingTaskInfo(cardTurnId, change ?? text, refining, true));
                    }
                    var streamed = CodingSteps.SplitResponse(snapshot);
                    timing.Note(streamed, questionEndTime);
                    if (streamed.StepsMarkdown.Length > 0)
                    {
                        Delegate?.ProcessorDidUpdateCodingTask(streamed.StepsMarkdown);
                    }
                    var card = CodingSteps.CardBullets(streamed, 0, false);
                    if (card.Length > 0)
                    {
                        Delegate?.ProcessorDidReceiveAnswerChunk(StripInlineMarkdownForCueCard(card), cardTurnId);
                    }
                });
            },
            AppConstants.MaxTokens.CodingSteps,
            ct).ConfigureAwait(false);

        if (!IsCurrentTurn(turnGeneration) || IsSuperseded()) return state.HasStarted;
        var raw = state.Text.Trim();
        if (!result.IsSuccess && !state.HasStarted) return false;
        if (result.IsSuccess && raw.Length == 0) return false;

        var response = CodingSteps.SplitResponse(raw);
        var progress = CodingSteps.ParseStreaming(response.StepsMarkdown);
        var stepCount = progress.Steps.Count;
        // Code that stopped early must not pass for finished code.
        CodingTaskOutcome outcome;
        if (!result.IsSuccess)
        {
            outcome = CodingTaskOutcome.Incomplete("The connection dropped before the code was finished.");
        }
        else if (progress.LastStepIsOpen)
        {
            outcome = CodingTaskOutcome.Incomplete("The reply was cut off before the last step ended.");
        }
        else
        {
            outcome = CodingTaskOutcome.Complete;
        }
        var outcomeLabel = outcome.IsComplete ? "complete" : $"incomplete(\"{outcome.Reason}\")";
        DebugLog(LogCategory.Answer, $"💻 CODING: finished with {stepCount} step(s), {response.SayBullets.Count} talking point(s), {raw.Length} chars, outcome={outcomeLabel}");
        if (stepCount > 0 && outcome.IsComplete)
        {
            // The next follow-up ("in TypeScript", "another way") changes exactly this code.
            lock (processingStateLock)
            {
                if (generationToken == codingGenerationToken) codingSession.Store(response.StepsMarkdown);
            }
        }
        // With no steps and no SAY lines the model ignored the format; keep what it said as a readable card.
        var finalCard = stepCount == 0 && response.SayBullets.Count == 0
            ? ConversationalDisplayAnswer(raw)
            : CodingSteps.CardBullets(response, stepCount, outcome.IsComplete);
        if (!outcome.IsComplete && stepCount > 0)
        {
            finalCard += (finalCard.Length == 0 ? "" : "\n") + "- The code stopped early. Use Try again in the panel.";
        }
        var finalCardText = StripInlineMarkdownForCueCard(finalCard);
        var totalMs = MillisecondsSince(questionEndTime);
        await OnMain(() =>
        {
            DebugLog(LogCategory.Answer, $"💻 CODING TIMING: {timing.Summary} total={totalMs}ms");
            if (response.StepsMarkdown.Length > 0)
            {
                Delegate?.ProcessorDidUpdateCodingTask(response.StepsMarkdown);
            }
            Delegate?.ProcessorDidReceiveAnswerChunk(finalCardText, cardTurnId);
            Delegate?.ProcessorDidFinishAnswer(finalCardText, totalMs, cardTurnId);
            Delegate?.ProcessorDidFinishCodingTask(outcome);
        }).ConfigureAwait(false);
        ScheduleSummarizationIfNeeded(context, turnGeneration);
        return true;
    }

    /// Stream a direct answer-only Haiku response for a clear local question.
    /// The card is created only after the first visible answer chunk. If Haiku
    /// does not emit visible text quickly, cancel the visible path so the caller
    /// can use a backup stream without duplicate cards.
    private async Task<bool> StreamDirectHaikuAnswerAsync(
        string text,
        string topic,
        bool topicInherited,
        CaptureDevice source,
        AnthropicClient haiku,
        string userBackground,
        ConversationContext context,
        string? pinnedSolution,
        Guid turnId,
        int turnSequence,
        int turnGeneration,
        DateTime questionEndTime,
        bool cardAlreadyOpen,
        CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var normalized = NormalizedQuestionText(text);
        var isVagueFollowUp = IsVagueFollowUpPrompt(normalized, TechnicalQuestionTokens(normalized));
        var messageType = isVagueFollowUp ? MessageType.FollowUp : MessageType.Answer;
        string prompt;
        lock (processingStateLock)
        {
            prompt = DirectHaikuAnswerPrompt(text, topic, topicInherited, userBackground, context, pinnedSolution);
        }
        var state = new AnswerStreamState();

        // The stream runs on its own, as the macOS app's unstructured task does: only this method stops it.
        var streamCancel = new CancellationTokenSource();
        var streamTask = Task.Run(() => haiku.StreamTextMessageAsync(
            prompt,
            chunk =>
            {
                if (!IsCurrentTurn(turnGeneration) || state.AppendVisibleChunk(chunk) is not { } update) return;

                if (update.IsFirst && !cardAlreadyOpen)
                {
                    bool committed;
                    lock (processingStateLock)
                    {
                        committed = IsCurrentTurn(turnGeneration);
                        if (committed)
                        {
                            context.AddUtterance(text, topic, isQuestion: true, sequence: turnSequence);
                            lastAnswerTime = DateTime.UtcNow;
                        }
                    }
                    if (!committed)
                    {
                        state.Cancel();
                        return;
                    }
                }

                mainThread.Post(() =>
                {
                    if (!IsCurrentTurn(turnGeneration)) return;
                    if (update.IsFirst && !cardAlreadyOpen)
                    {
                        DebugLog(LogCategory.Delegate, $"Calling processorDidReceiveQuestion for '{text.Prefix(50)}...'");
                        Delegate?.ProcessorDidReceiveQuestion(text, topic, messageType, source, turnId, turnSequence);
                        var cardStartLatencyMs = MillisecondsSince(questionEndTime);
                        DebugLog(LogCategory.Delegate, $"Calling processorDidStartStreaming with cardStart={cardStartLatencyMs}ms");
                        Delegate?.ProcessorDidStartStreaming(messageType, topic, cardStartLatencyMs, turnId, turnSequence);
                    }

                    if (update.Snapshot.Length < 100 || update.Snapshot.Length % 200 == 0)
                    {
                        DebugLog(LogCategory.Stream, $"Direct Haiku chunk received, total: {update.Snapshot.Length} chars");
                    }
                    Delegate?.ProcessorDidReceiveAnswerChunk(StripInlineMarkdownForCueCard(update.Snapshot), turnId);
                });
            },
            AppConstants.MaxTokens.AnswerStream,
            streamCancel.Token), CancellationToken.None);
        _ = streamTask.ContinueWith(static finished => _ = finished.Exception, TaskContinuationOptions.OnlyOnFaulted);

        void Abandon()
        {
            state.Cancel();
            streamCancel.Cancel();
        }

        var firstChunkTimeout = AppSettings.Shared.AnswerModel == AnswerModel.Gpt
            ? AppConstants.Thresholds.DirectGptFirstChunkTimeout
            : AppConstants.Thresholds.DirectHaikuFirstChunkTimeout;
        while (!state.HasStarted)
        {
            if (!IsCurrentTurn(turnGeneration) || ct.IsCancellationRequested)
            {
                Abandon();
                return false;
            }
            if ((DateTime.UtcNow - startTime).TotalSeconds >= firstChunkTimeout)
            {
                Abandon();
                DebugLog(LogCategory.Answer, $"Direct Haiku first chunk timeout after {(int)(firstChunkTimeout * 1000)}ms");
                return false;
            }
            try
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Abandon();
                return false;
            }
        }

        Outcome result;
        try
        {
            result = await streamTask.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            result = Outcome.Failure(error);
        }
        if (!IsCurrentTurn(turnGeneration) || ct.IsCancellationRequested)
        {
            state.Cancel();
            return false;
        }
        var accumulated = state.Text;
        if (accumulated.Trim().Length == 0)
        {
            if (result.Error is { } emptyError)
            {
                DebugLog(LogCategory.Error, $"Direct Haiku answer produced no text: {Describe(emptyError)}");
            }
            return false;
        }

        if (result.Error is { } streamError)
        {
            DebugLog(LogCategory.Error, $"Direct Haiku stream ended after visible text with error: {Describe(streamError)}");
        }

        var answerLatencyMs = MillisecondsSince(questionEndTime);
        var totalLatency = (DateTime.UtcNow - startTime).TotalSeconds * 1000;
        DebugLog(LogCategory.Answer, $"Direct Haiku answer complete ({(int)totalLatency}ms), {accumulated.Length} chars");

        var cleaned = ConversationalDisplayAnswer(accumulated);
        await OnMain(() =>
        {
            if (cleaned != accumulated) Delegate?.ProcessorDidReceiveAnswerChunk(cleaned, turnId);
            Delegate?.ProcessorDidFinishAnswer(cleaned, answerLatencyMs, turnId);
        }).ConfigureAwait(false);

        ScheduleSummarizationIfNeeded(context, turnGeneration);

        return true;
    }

    /// Stream a concise answer from the Groq backup model for a clear,
    /// concrete-topic local question. The question bubble and answer card are
    /// committed on the first chunk so the card appears already populated.
    /// Returns true if answer text was shown (turn handled); false if nothing was
    /// emitted, so the caller can fall back to the model path without a duplicate card.
    private async Task<bool> StreamProvisionalAnswerAsync(
        string text,
        string topic,
        bool topicInherited,
        CaptureDevice source,
        GroqInterviewClient groq,
        string userBackground,
        ConversationContext context,
        string? pinnedSolution,
        Guid turnId,
        int turnSequence,
        int turnGeneration,
        DateTime questionEndTime,
        CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var state = new AnswerStreamState();

        // Vague follow-ups ("tell me more", "go deeper") resolve their topic from the
        // prior turn (see ProvisionalAnswerTopic). Tag them as follow-ups and pass the
        // recent conversation so the fast model adds new angles instead of repeating the
        // earlier answer. Concrete questions stay fresh answers with no extra context.
        var normalized = NormalizedQuestionText(text);
        var isVagueFollowUp = IsVagueFollowUpPrompt(normalized, TechnicalQuestionTokens(normalized));
        var messageType = isVagueFollowUp ? MessageType.FollowUp : MessageType.Answer;
        // The recent conversation always goes in so references like "those methods" resolve; it is
        // labelled as context for the current question only.
        string? recentConversation;
        lock (processingStateLock) recentConversation = RecentConversationForPrompt(context);

        var result = await groq.StreamAnswerAsync(
            topic,
            text,
            chunk =>
            {
                if (!IsCurrentTurn(turnGeneration)) return;
                if (state.AppendVisibleChunk(chunk) is not { } update) return;
                if (update.IsFirst)
                {
                    bool committed;
                    lock (processingStateLock)
                    {
                        committed = IsCurrentTurn(turnGeneration);
                        if (committed)
                        {
                            context.AddUtterance(text, topic, isQuestion: true, sequence: turnSequence);
                            lastAnswerTime = DateTime.UtcNow;
                        }
                    }
                    if (!committed)
                    {
                        state.Cancel();
                        return;
                    }
                }
                var snapshot = update.Snapshot;
                var isFirst = update.IsFirst;
                mainThread.Post(() =>
                {
                    if (!IsCurrentTurn(turnGeneration)) return;
                    if (isFirst)
                    {
                        DebugLog(LogCategory.Delegate, $"Calling processorDidReceiveQuestion for '{text.Prefix(50)}...'");
                        Delegate?.ProcessorDidReceiveQuestion(text, topic, messageType, source, turnId, turnSequence);
                        var cardStartLatencyMs = MillisecondsSince(questionEndTime);
                        DebugLog(LogCategory.Delegate, $"Calling processorDidStartStreaming with cardStart={cardStartLatencyMs}ms");
                        Delegate?.ProcessorDidStartStreaming(messageType, topic, cardStartLatencyMs, turnId, turnSequence);
                    }
                    if (snapshot.Length < 100 || snapshot.Length % 200 == 0)
                    {
                        DebugLog(LogCategory.Stream, $"Chunk received, total: {snapshot.Length} chars");
                    }
                    Delegate?.ProcessorDidReceiveAnswerChunk(StripInlineMarkdownForCueCard(snapshot), turnId);
                });
            },
            userBackground.Length == 0 ? null : userBackground,
            recentConversation,
            isVagueFollowUp,
            topicInherited,
            pinnedSolution,
            ct).ConfigureAwait(false);

        if (!IsCurrentTurn(turnGeneration) || ct.IsCancellationRequested)
        {
            state.Cancel();
            return false;
        }
        var accumulated = state.Text;
        if (!state.HasStarted || accumulated.Trim().Length == 0)
        {
            if (result.Error is { } error)
            {
                DebugLog(LogCategory.Error, $"Provisional answer produced nothing, falling back: {Describe(error)}");
            }
            return false;
        }

        var answerLatencyMs = MillisecondsSince(questionEndTime);
        var totalLatency = (DateTime.UtcNow - startTime).TotalSeconds * 1000;
        DebugLog(LogCategory.Answer, $"Answer complete ({(int)totalLatency}ms), {accumulated.Length} chars");

        var cleaned = ConversationalDisplayAnswer(accumulated);
        await OnMain(() =>
        {
            if (cleaned != accumulated) Delegate?.ProcessorDidReceiveAnswerChunk(cleaned, turnId);
            Delegate?.ProcessorDidFinishAnswer(cleaned, answerLatencyMs, turnId);
        }).ConfigureAwait(false);

        ScheduleSummarizationIfNeeded(context, turnGeneration);

        return true;
    }

    // Standalone Answer Generation

    internal static string ManualAnswerPrompt(
        string question, string topic, string userBackground, ConversationContext context, string? pinnedSolution, string? memoryContext)
    {
        var backgroundContext = Briefing.ResumeGroundingInstruction(userBackground, question, topic, context.GetContextForLLM());
        if (backgroundContext.Length > 0) backgroundContext += "\n\n";

        // Inject memory from past sessions
        if (memoryContext is not null) backgroundContext += $"{memoryContext}\n\n";

        var conversationHistory = context.GetFullConversation();
        var topicsSummary = context.GetTopicsSummary();
        var historyContext = conversationHistory.Length == 0
            ? ""
            : $"\nINTERVIEW SO FAR:\n{conversationHistory}\n{topicsSummary}\n";

        var pinnedContext = pinnedSolution is null
            ? ""
            : $"\nCURRENT CODING TASK SOLUTION (pinned above):\n{pinnedSolution}\n\nIf the question relates to this solution, answer in context of it.\n";

        var settings = AppSettings.Shared;
        var languageInstruction = settings.LlmLanguageInstruction;
        return $"""
            You are helping someone who is BEING INTERVIEWED for a software engineering position.
            They need quick, glanceable answers to help them respond to the interviewer.

            {settings.InterviewContext}

            {settings.AnswerStyleInstruction}

            {backgroundContext}{historyContext}{pinnedContext}CURRENT QUESTION: "{question}"
            Topic: {topic}

            SPEECH-TO-TEXT: The question text is from voice transcription and may contain errors.
            ALWAYS answer about the Topic shown above. Ignore garbled words.
            Example: "What is key developer?" with Topic: hashmap → Answer about HashMap keys.

            FORBIDDEN PHRASES (never use these):
            - "doesn't exist", "you might mean", "you might be thinking of"
            - "ask them to clarify", "could you clarify", "did you mean"
            - "I think you're asking about", "possible intended question"

            Just answer the topic directly and confidently.

            FORMAT (pick best for quick scanning):
            - Comparisons: X: [brief] | Y: [brief]
            - Definitions: one sentence + 1-3 bullets
            - Code: `command` + one line why

            {languageInstruction}
            """;
    }

    /// Generate answer using Haiku with streaming (for manual invocation)
    public async Task StreamAnswerWithHaikuAsync(string question, string topic, MessageType messageType, CancellationToken ct = default)
    {
        var turnId = Guid.NewGuid();
        var turnSequence = ReserveTurnSequence();
        var generation = CurrentTurnGeneration();
        if (anthropicClient is not { } haiku)
        {
            Log.Print("❌ Anthropic client not configured!");
            return;
        }

        var userBackground = Delegate?.UserBackground ?? "";
        if (Delegate?.ConversationContext is not { } context) return;

        var prompt = ManualAnswerPrompt(
            question, topic, userBackground, context, Delegate?.PinnedSolution, memoryRetrieval.Retrieve(new[] { topic }));

        // Create empty streaming message on main thread
        await OnMain(() =>
        {
            Delegate?.ProcessorShowLoading("💭 Generating answer...", LoadingColor.AppleGreen, turnId);
            Delegate?.ProcessorDidReceiveQuestion(question, topic, messageType, CaptureDevice.SystemAudio, turnId, turnSequence);
            Delegate?.ProcessorDidStartStreaming(messageType, topic, null, turnId, turnSequence);
        }).ConfigureAwait(false);

        var startTime = DateTime.UtcNow;
        var manualState = new AnswerStreamState();

        var result = await haiku.StreamTextMessageAsync(
            prompt,
            chunk =>
            {
                if (!IsCurrentTurn(generation) || manualState.AppendVisibleChunk(chunk) is not { } update) return;
                var snapshot = update.Snapshot;
                mainThread.Post(() =>
                {
                    if (!IsCurrentTurn(generation)) return;
                    Delegate?.ProcessorDidReceiveAnswerChunk(StripInlineMarkdownForCueCard(snapshot), turnId);
                });
            },
            AppConstants.MaxTokens.AnswerStream,
            ct).ConfigureAwait(false);

        var latency = (DateTime.UtcNow - startTime).TotalSeconds * 1000;
        if (!IsCurrentTurn(generation) || ct.IsCancellationRequested)
        {
            manualState.Cancel();
            await HideLoading(turnId).ConfigureAwait(false);
            return;
        }
        var manualStreamingContent = manualState.Text;

        if (result.Error is { } error)
        {
            Log.Print($"❌ Streaming error: {Describe(error)}");
            await OnMain(() =>
            {
                Delegate?.ProcessorDidReceiveAnswerChunk($"Error: {Describe(error)}", turnId);
                Delegate?.ProcessorHideLoading(turnId);
            }).ConfigureAwait(false);
            return;
        }

        Log.Print($"💡 Answer (Haiku {(int)latency}ms): {manualStreamingContent.Prefix(100)}...");
        var cleanedAnswer = ConversationalDisplayAnswer(manualStreamingContent);
        await OnMain(() =>
        {
            if (cleanedAnswer != manualStreamingContent) Delegate?.ProcessorDidReceiveAnswerChunk(cleanedAnswer, turnId);
            Delegate?.ProcessorDidFinishAnswer(cleanedAnswer, (int)latency, turnId);
            Delegate?.ProcessorHideLoading(turnId);
        }).ConfigureAwait(false);
    }
}
