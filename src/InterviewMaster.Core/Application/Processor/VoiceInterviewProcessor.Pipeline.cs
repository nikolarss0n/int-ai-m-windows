using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.AppServices;

/// The path from a finished audio segment to an answer card.
public partial class VoiceInterviewProcessor
{
    /// What the macOS app shows for a cancelled turn; kept so both apps read the same.
    private const string CancelledDescription = "The operation couldn’t be completed. (Swift.CancellationError error 1.)";

    private static string Describe(Exception error) => error is OperationCanceledException ? CancelledDescription : error.Message;

    private static string Describe(CodingTurnDecision decision) => decision.Kind switch
    {
        CodingTurnKind.ArmSession => "armSession",
        CodingTurnKind.NeedsScreenContext => "needsScreenContext",
        CodingTurnKind.NewTask => $"newTask(\"{decision.Text}\")",
        CodingTurnKind.RefineTask => $"refineTask(\"{decision.Text}\")",
        _ => $"changeCode(\"{decision.Text}\")"
    };

    private static int MillisecondsSince(DateTime start) => (int)((DateTime.UtcNow - start).TotalSeconds * 1000);

    private static string Joined(string buffer, string text) => buffer.Length == 0 ? text : $"{buffer} {text}";

    private Task OnMain(Action action) => mainThread.RunAsync(action);

    private Task HideLoading(Guid turnId) => mainThread.RunAsync(() => Delegate?.ProcessorHideLoading(turnId));

    /// Registers the work under its id, runs it in the background, and forgets it when it ends.
    private void StartTurnTask(Guid id, Func<CancellationToken, Task> body)
    {
        var turnTask = new TurnTask();
        lock (turnLock) processTasks[id] = turnTask;
        turnTask.Task = Task.Run(async () =>
        {
            try
            {
                await body(turnTask.Token).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                DebugLog(LogCategory.Error, $"Turn task failed: {Describe(error)}");
            }
            finally
            {
                FinishProcessTask(id);
            }
        });
    }

    private void FinishProcessTask(Guid turnId)
    {
        lock (turnLock) processTasks.Remove(turnId);
    }

    /// Holds text for the next turn to join, unless a newer turn or a source switch owns the buffer.
    private bool HoldInBuffer(string text, int bufferGenerationForTurn, int turnSequence)
    {
        lock (processingStateLock)
        {
            if (bufferGenerationForTurn != bufferGeneration) return false;
            if (utteranceBufferSequence is { } held && turnSequence < held) return false;
            utteranceBuffer = text;
            bufferTimestamp = bufferNow();
            utteranceBufferSequence = turnSequence;
            return true;
        }
    }

    /// Empties the buffer when it still holds the fragment this turn read.
    private void ReleaseBufferSnapshot(int bufferGenerationForTurn, int? bufferSnapshotSequence)
    {
        lock (processingStateLock)
        {
            if (bufferGenerationForTurn != bufferGeneration) return;
            if (bufferSnapshotSequence is not { } sequence || utteranceBufferSequence != sequence) return;
            utteranceBuffer = "";
            bufferTimestamp = null;
            utteranceBufferSequence = null;
        }
    }

    /// Process an audio segment through the full pipeline
    public void ProcessAudioSegment(byte[] audioData, CaptureDevice source)
    {
        int bufferGenerationForTurn;
        lock (processingStateLock) bufferGenerationForTurn = bufferGeneration;
        var turnId = Guid.NewGuid();
        var turnSequence = ReserveTurnSequence();
        var sourceLabel = source == CaptureDevice.Microphone ? "🎤 MIC" : "🔊 SYS";
        DebugLog(LogCategory.Audio, $"{sourceLabel} processAudioSegment called with {audioData.Length} bytes");
        if (!SpeechTurnPolicy.StartsAnswerCard(SpeechTurnPolicy.Action(SpeechTurnEmit.FinalSilence)))
        {
            DebugLog(LogCategory.Error, "SpeechTurnPolicy blocked answer commit");
            return;
        }
        if (groqClient is not { } client)
        {
            DebugLog(LogCategory.Error, "groqClient is nil!");
            return;
        }
        DebugLog(LogCategory.Audio, $"groqClient configured: {client.GetType().Name}");

        int generation;
        TurnTask? previewForTurn;
        int previewGenerationForTurn;
        lock (turnLock)
        {
            generation = turnGeneration;
            previewForTurn = previewTask;
            previewGenerationForTurn = previewGeneration;
        }

        StartTurnTask(turnId, ct => ProcessTurnAsync(
            audioData, source, client, turnId, turnSequence, generation, bufferGenerationForTurn,
            previewForTurn, previewGenerationForTurn, ct));
    }

    private async Task ProcessTurnAsync(
        byte[] audioData,
        CaptureDevice source,
        GroqInterviewClient client,
        Guid turnId,
        int turnSequence,
        int generation,
        int bufferGenerationForTurn,
        TurnTask? previewForTurn,
        int previewGenerationForTurn,
        CancellationToken ct)
    {
        try
        {
            // 1. Transcribe audio + warmup Anthropic connection in parallel
            await OnMain(() => Delegate?.ProcessorShowLoading("🎙️ Transcribing...", LoadingColor.SystemBlue, turnId)).ConfigureAwait(false);
            DebugLog(LogCategory.Transcription, $"Sending {audioData.Length} bytes to Groq...");

            _ = Task.Run(async () =>
            {
                await client.WarmupConnectionAsync().ConfigureAwait(false);
                if (anthropicClient is { } warmed) await warmed.WarmupConnectionAsync().ConfigureAwait(false);
            });

            if (previewForTurn is not null) await previewForTurn.Task.ConfigureAwait(false);

            string transcription;
            if (CachedPreviewTranscript(audioData, previewGenerationForTurn) is { } cached)
            {
                transcription = cached;
                DebugLog(LogCategory.Transcription, $"Reusing preview STT (0ms): '{transcription}'");
            }
            else
            {
                var stt = await TranscribeWithTimeoutAsync(client, audioData, ct).ConfigureAwait(false);
                transcription = stt.Text;
                DebugLog(LogCategory.Transcription, $"Result ({(int)stt.LatencyMs}ms): '{transcription}'");
            }
            if (!IsCurrentTurn(generation))
            {
                DebugLog(LogCategory.Audio, "PROCESS: dropped stale turn after STT");
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            // Per-turn latency anchor; rapid turns must not overwrite each other.
            var questionEndTime = DateTime.UtcNow;

            var rawTrimmed = transcription.Trim();
            var trimmed = RepairNoisyTechnicalTranscript(rawTrimmed);
            if (trimmed != rawTrimmed)
            {
                DebugLog(LogCategory.Transcription, $"Repaired noisy AI transcript: '{rawTrimmed}' -> '{trimmed}'");
            }
            if (trimmed.Length == 0)
            {
                Log.Print("⚠️ PROCESS: SKIPPED - Empty transcription after trimming");
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            // Deduplication check - skip if similar text was just processed
            var initialSignal = QuestionSignalFor(trimmed, Delegate?.ConversationContext.LastTopic);
            var initialReasons = string.Join(",", initialSignal.Reasons);
            if (initialSignal.ProtectsFromSkip)
            {
                Log.Print($"🧭 SIGNAL: score={initialSignal.Score} reasons={initialReasons} topic={initialSignal.TopicHint ?? "nil"}");
            }

            if (IsDuplicateTranscription(trimmed, source, initialSignal))
            {
                Log.Print("🔄 PROCESS: SKIPPED - Duplicate transcription");
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            Log.Print($"📝 PROCESS: Trimmed text ({trimmed.Length} chars): '{trimmed}'");

            // Filter Whisper hallucinations (common artifacts from silence/noise)
            if (IsWhisperHallucination(trimmed))
            {
                Log.Print($"👻 PROCESS: SKIPPED - Whisper hallucination: '{trimmed}'");
                Log.Print($"👻 Whisper hallucination filtered: {trimmed}");
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            // Filter non-ASCII garbage when language is English
            if (AppSettings.Shared.ListeningLanguage == SpeakingLanguage.English)
            {
                var nonAsciiCount = trimmed.EnumerateRunes().Count(rune => !rune.IsAscii);
                var nonAsciiRatio = (float)nonAsciiCount / Math.Max(trimmed.Length, 1);
                if (nonAsciiRatio > 0.15f && nonAsciiCount > 3 && !initialSignal.ProtectsFromSkip)
                {
                    Log.Print($"👻 PROCESS: SKIPPED - Non-ASCII garbage ({F0(nonAsciiRatio * 100)}% non-ASCII): '{trimmed}'");
                    Log.Print($"👻 Non-ASCII hallucination filtered ({(int)(nonAsciiRatio * 100)}% non-ASCII): {trimmed}");
                    await HideLoading(turnId).ConfigureAwait(false);
                    return;
                }
            }

            // Filter very short transcriptions (likely noise)
            if (trimmed.Length < 5 && !Has(trimmed, "?") && !initialSignal.ProtectsFromSkip)
            {
                Log.Print($"👻 PROCESS: SKIPPED - Too short ({trimmed.Length} chars), no '?': '{trimmed}'");
                Log.Print($"👻 Too short, likely noise: {trimmed}");
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            Log.Print("✅ PROCESS: Passed hallucination/length filters, proceeding...");

            DebugLog(LogCategory.Classification, "Captured interviewer audio - checking filters...");

            if (ShouldSkipAsFillerOrGreeting(trimmed) || ShouldSkipAsSocialPleasantry(trimmed))
            {
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            if (ConversationUnderstanding.IsCandidateMetaUtterance(trimmed))
            {
                Log.Print($"🗣️ PROCESS: SKIPPED - Candidate stalling or check-in: '{trimmed}'");
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            // Skip very short utterances (< 4 chars and no question mark)
            var normalizedText = trimmed.Lowercased().TrimInline();
            if (normalizedText.Length < 4 && !Has(normalizedText, "?") && !initialSignal.ProtectsFromSkip)
            {
                Log.Print($"⚡ PROCESS: LOCAL SKIP - Too short: '{trimmed}'");
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            var bufferSnapshot = "";
            int? bufferSnapshotSequence = null;
            lock (processingStateLock)
            {
                if (bufferGenerationForTurn == bufferGeneration)
                {
                    if (bufferTimestamp is { } timestamp
                        && utteranceBuffer.Length > 0
                        && (bufferNow() - timestamp).TotalSeconds > BufferTimeout)
                    {
                        Log.Print($"📦 PROCESS: Clearing stale buffer before classification: '{utteranceBuffer}'");
                        utteranceBuffer = "";
                        bufferTimestamp = null;
                        utteranceBufferSequence = null;
                    }
                    if (utteranceBufferSequence == turnSequence - 1)
                    {
                        bufferSnapshot = utteranceBuffer;
                        bufferSnapshotSequence = utteranceBufferSequence;
                    }
                }
            }

            // Garbled speech is never guessed at. A question already held is still answered after
            // the silence; with nothing held, say so and let the interviewer repeat it.
            if (ConversationUnderstanding.LooksGarbled(trimmed) && initialSignal.TopicHint is null)
            {
                Log.Print($"🙉 PROCESS: SKIPPED - Garbled transcription: '{trimmed}'");
                var questionStillHeld = ConversationUnderstanding.HeldQuestion(bufferSnapshot) is not null;
                await OnMain(() =>
                {
                    Delegate?.ProcessorHideLoading(turnId);
                    if (!questionStillHeld) Delegate?.ProcessorDidUpdateStatus(FeedbackCopy.DidntCatchThat);
                }).ConfigureAwait(false);
                return;
            }

            DebugLog(LogCategory.Classification, "Proceeding to classification...");

            // Clear questions keep the direct answer route. Ambiguous turns use
            // the combined stream by default; the opt-in Jev experiment can select
            // an answer-only stream, with this combined path as its fallback.
            if (anthropicClient is not { } haiku)
            {
                DebugLog(LogCategory.Error, "anthropicClient is nil!");
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }
            DebugLog(LogCategory.Classification, $"anthropicClient configured: {haiku.GetType().Name}");

            await OnMain(() => Delegate?.ProcessorShowLoading("🔍 Analyzing...", LoadingColor.ApplePurple, turnId)).ConfigureAwait(false);

            // Get context for the combined call
            var userBackground = await mainThread.RunAsync(() => Delegate?.UserBackground ?? "").ConfigureAwait(false);
            var pinnedSolution = Delegate?.PinnedSolution;
            if (Delegate?.ConversationContext is not { } context)
            {
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            string? lastTopic;
            lock (processingStateLock) lastTopic = context.LastTopic;

            // FAST ANSWER PATH: a strong, complete, concrete-topic local
            // question streams an answer immediately, so useful text reaches
            // the card without waiting for the STATUS/--- classifier parser.
            // Groq is tried first for lower first-token latency; Haiku remains
            // the quality fallback if Groq emits nothing.
            if (!IsCurrentTurn(generation))
            {
                DebugLog(LogCategory.Audio, "PROCESS: dropped stale turn before answer");
                return;
            }

            // A request spoken in pieces ("Write a function that ..." / "reverses a string") is judged as one.
            var codingText = Joined(bufferSnapshot, trimmed);
            var previousQuestion = context.LastInterviewerQuestion;
            CodingTurnDecision? codingDecision;
            lock (processingStateLock)
            {
                codingDecision = CodingSteps.TurnDecision(codingText, codingSession, DateTime.UtcNow, previousQuestion);
            }
            if (codingDecision is { } decision)
            {
                DebugLog(LogCategory.Answer, $"💻 CODING TURN: {Describe(decision)}");
                // The buffered fragment is now part of this turn.
                ReleaseBufferSnapshot(bufferGenerationForTurn, bufferSnapshotSequence);
                var handled = await HandleCodingTurnAsync(
                    decision, codingText, source, haiku, context, turnId, turnSequence, generation, questionEndTime, ct).ConfigureAwait(false);
                if (handled)
                {
                    PublishBriefing(trimmed, true, userBackground);
                    await HideLoading(turnId).ConfigureAwait(false);
                    return;
                }
            }

            // A clear question that stops mid-thought is held before any model call, so the next
            // piece joins it however quickly it arrives.
            var textSoFar = Joined(bufferSnapshot, trimmed);
            var signalSoFar = QuestionSignalFor(textSoFar, lastTopic);
            if (!signalSoFar.IsBareIncomplete
                && ShouldTreatLocalSignalAsClearQuestion(textSoFar, signalSoFar)
                && ConversationUnderstanding.HeldQuestion(textSoFar) is { } trailingQuestion
                && HoldInBuffer(textSoFar, bufferGenerationForTurn, turnSequence))
            {
                Log.Print($"📦 PROCESS: BUFFERED - Question trails off: '{textSoFar}'");
                AnswerBufferedQuestionAfterSilence(
                    trailingQuestion,
                    textSoFar,
                    CorrectedTopic(trailingQuestion, signalSoFar.TopicHint ?? "unknown"),
                    source, haiku, context, turnSequence, bufferGenerationForTurn, generation);
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            // A trailing fragment right after a question ("Kind of, let's say.") carries it on,
            // whatever a model would call it alone.
            if (bufferSnapshot.Length == 0 && ConversationUnderstanding.TrailsOff(trimmed))
            {
                double? sincePrevious;
                lock (processingStateLock)
                {
                    sincePrevious = lastAnswerTime is { } answered ? (DateTime.UtcNow - answered).TotalSeconds : null;
                }
                if (ConversationUnderstanding.ContinuedQuestion(
                        context.LastInterviewerQuestion, trimmed, sincePrevious, AppConstants.Thresholds.QuestionContinuationWindow) is { } joined
                    && HoldInBuffer(joined, bufferGenerationForTurn, turnSequence))
                {
                    Log.Print($"📦 PROCESS: BUFFERED - Fragment continues the last question: '{joined}'");
                    await HideLoading(turnId).ConfigureAwait(false);
                    return;
                }
            }

            if (bufferSnapshot.Length == 0 && ProvisionalPlan(trimmed, lastTopic, userBackground.Length > 0) is { } plan)
            {
                var provisionalTopic = plan.Topic;
                // Quality mode: the user picked GPT, so never hand clear questions to the small Groq model.
                var useGroqFastPath = AppSettings.Shared.AnswerModel != AnswerModel.Gpt;
                DebugLog(LogCategory.Answer, $"⚡ FAST PATH: answer-only stream for topic='{provisionalTopic}' inherited={Flag(plan.InheritedFromPriorTurn)} via {(useGroqFastPath ? "Groq" : "selected model")}");
                var groqHandled = useGroqFastPath && await StreamProvisionalAnswerAsync(
                    trimmed, provisionalTopic, plan.InheritedFromPriorTurn, source, client, userBackground, context,
                    pinnedSolution, turnId, turnSequence, generation, questionEndTime, ct).ConfigureAwait(false);
                if (groqHandled)
                {
                    PublishBriefing(trimmed, true, userBackground);
                    await HideLoading(turnId).ConfigureAwait(false);
                    return;
                }

                DebugLog(LogCategory.Answer, "⚡ FAST GROQ PATH: no visible text, trying direct Haiku fallback");
                var haikuHandled = await StreamDirectHaikuAnswerAsync(
                    trimmed, provisionalTopic, plan.InheritedFromPriorTurn, source, haiku, userBackground, context,
                    pinnedSolution, turnId, turnSequence, generation, questionEndTime, false, ct).ConfigureAwait(false);
                if (haikuHandled)
                {
                    PublishBriefing(trimmed, true, userBackground);
                    await HideLoading(turnId).ConfigureAwait(false);
                    return;
                }

                DebugLog(LogCategory.Answer, "⚡ FAST PATH: yielded nothing, falling back to Haiku classifier");
            }

            // Build multi-turn messages (limited to recent context)
            List<Dictionary<string, string>> messagesForApi;
            lock (processingStateLock)
            {
                var multiTurnMessages = context.BuildMultiTurnMessages(trimmed, pinnedSolution);
                messagesForApi = context.MessagesToAPIFormat(multiTurnMessages);
            }

            // State for handling classification result
            var shouldStreamAnswer = false;
            var detectedTopic = "unknown";
            var messageType = MessageType.Answer;
            var fullText = "";
            var turnStreamState = new AnswerStreamState();

            var startTime = DateTime.UtcNow;

            void OnClassification(AnthropicClient.UtteranceClassification classification)
            {
                if (!IsCurrentTurn(generation)) return;
                lock (processingStateLock)
                {
                    if (!IsCurrentTurn(generation)) return;
                    var latency = (DateTime.UtcNow - startTime).TotalSeconds * 1000;
                    DebugLog(LogCategory.Classification, $"Result ({(int)latency}ms): status='{classification.Status}', topic='{classification.Topic ?? "nil"}'");

                    // Handle filler words
                    if (classification.Status == "filler")
                    {
                        if (initialSignal.ProtectsFromSkip)
                        {
                            Log.Print($"⚠️ PROCESS: OVERRIDE - Strong local signal despite filler status ({initialReasons})");
                        }
                        else
                        {
                            Log.Print($"🗣️ PROCESS: SKIPPED - Filler word detected: '{trimmed}'");
                            return;
                        }
                    }

                    // Comma-ending override
                    var combinedForCheck = Joined(bufferSnapshot, trimmed);
                    var continuedQuestion = bufferSnapshot.Length == 0
                        ? ConversationUnderstanding.ContinuedQuestion(
                            context.LastInterviewerQuestion,
                            trimmed,
                            lastAnswerTime is { } answered ? (DateTime.UtcNow - answered).TotalSeconds : null,
                            AppConstants.Thresholds.QuestionContinuationWindow)
                        : null;
                    var textToHold = continuedQuestion ?? combinedForCheck;
                    var endsWithComma = combinedForCheck.TrimInline().HasSuffix(",");
                    var combinedSignal = QuestionSignalFor(combinedForCheck, context.LastTopic);
                    if (classification.Status == "question" && endsWithComma && !combinedSignal.ProtectsFromSkip)
                    {
                        Log.Print("⚠️ PROCESS: OVERRIDE - Ends with comma, treating as incomplete");
                        // The model heard a question, so silence answers it even when the local
                        // signal missed it ("whatisObject.freeze,").
                        if (HoldInBuffer(textToHold, bufferGenerationForTurn, turnSequence)
                            && continuedQuestion is null
                            && !combinedSignal.IsBareIncomplete
                            && !ShouldVetoQuestionAsCandidateStatement(combinedForCheck, combinedSignal)
                            && ConversationUnderstanding.HeldQuestion(combinedForCheck) is { } commaQuestion)
                        {
                            AnswerBufferedQuestionAfterSilence(
                                commaQuestion,
                                combinedForCheck,
                                CorrectedTopic(combinedForCheck, classification.Topic),
                                source, haiku, context, turnSequence, bufferGenerationForTurn, generation);
                        }
                        return;
                    }

                    // Complete utterance
                    fullText = Joined(bufferSnapshot, trimmed);
                    ReleaseBufferSnapshot(bufferGenerationForTurn, bufferSnapshotSequence);
                    var localSignal = QuestionSignalFor(fullText, context.LastTopic);
                    var localReasons = string.Join(",", localSignal.Reasons);
                    var hasClearQuestionMarker = ShouldTreatLocalSignalAsClearQuestion(fullText, localSignal);
                    var shouldBufferLocalIncomplete = localSignal.IsBareIncomplete || IsLocallyIncomplete(fullText);
                    detectedTopic = CorrectedTopic(fullText, classification.Topic);
                    if (detectedTopic.Lowercased() == "unknown" && localSignal.TopicHint is { } topicHint)
                    {
                        detectedTopic = topicHint;
                    }

                    if (classification.Status == "question" && shouldBufferLocalIncomplete)
                    {
                        HoldInBuffer(textToHold, bufferGenerationForTurn, turnSequence);
                        Log.Print("📦 PROCESS: BUFFERED - Locally incomplete question");
                        if (continuedQuestion is null
                            && !localSignal.IsBareIncomplete
                            && !ShouldVetoQuestionAsCandidateStatement(fullText, localSignal)
                            && ConversationUnderstanding.HeldQuestion(fullText) is { } incompleteQuestion)
                        {
                            AnswerBufferedQuestionAfterSilence(
                                incompleteQuestion, fullText, detectedTopic,
                                source, haiku, context, turnSequence, bufferGenerationForTurn, generation);
                        }
                        return;
                    }

                    if (classification.Status == "question" && ShouldVetoQuestionAsCandidateStatement(fullText, localSignal))
                    {
                        Log.Print($"🔊 PROCESS: SKIPPED - Candidate-style explanation, not interviewer question: '{fullText}'");
                        context.AddUtterance(fullText, detectedTopic, sequence: turnSequence);
                        return;
                    }

                    if (classification.Status == "incomplete" && hasClearQuestionMarker)
                    {
                        Log.Print($"⚠️ PROCESS: OVERRIDE - Clear question signal despite incomplete status ({localReasons})");
                    }
                    else if (classification.Status == "incomplete")
                    {
                        HoldInBuffer(textToHold, bufferGenerationForTurn, turnSequence);
                        Log.Print("📦 PROCESS: BUFFERED - Incomplete utterance");
                        return;
                    }

                    // Promote short Bulgarian/mixed technical prompts that Whisper transcribes without a question mark.
                    if (classification.Status is "answer" or "statement")
                    {
                        if (hasClearQuestionMarker)
                        {
                            Log.Print($"⚠️ PROCESS: OVERRIDE - Clear question signal despite status '{classification.Status}' ({localReasons})");
                        }
                        else
                        {
                            Log.Print("🔊 PROCESS: Interviewer statement (not a question)");
                            context.AddUtterance(fullText, detectedTopic, sequence: turnSequence);
                            PublishBriefing(fullText, false, userBackground);
                            return;
                        }
                    }

                    // Check cooldown - only skip if within cooldown, the model did not hear a question,
                    // and there are no clear question markers
                    if (lastAnswerTime is { } lastAnswer
                        && (DateTime.UtcNow - lastAnswer).TotalSeconds < AnswerCooldown
                        && !(hasClearQuestionMarker || classification.Status == "question"))
                    {
                        Log.Print("⏸️ PROCESS: SKIPPED - Cooldown active, no question markers");
                        context.AddUtterance(fullText, detectedTopic, sequence: turnSequence);
                        PublishBriefing(fullText, false, userBackground);
                        return;
                    }

                    // Determine message type based on topic
                    var topicLower = detectedTopic.Lowercased();
                    var contextTopic = context.LastTopic;
                    if (topicLower == "followup" && contextTopic is not null)
                    {
                        messageType = MessageType.FollowUp;
                        detectedTopic = contextTopic;
                    }
                    else if (topicLower == "followup")
                    {
                        // Orphan follow-up with no prior context - treat as new question
                        detectedTopic = "unknown";
                    }
                    else if (topicLower == "unknown" && contextTopic is not null && ConversationUnderstanding.RefersBackToConversation(fullText))
                    {
                        // Unknown topic that leans on the previous turn - treat as follow-up.
                        // A concrete new question stays "unknown" so the stale topic is not stamped on it.
                        messageType = MessageType.FollowUp;
                        detectedTopic = contextTopic;
                    }

                    // All checks passed - enable answer streaming
                    DebugLog(LogCategory.Answer, $"✅ Passed all filters! Will stream answer for topic='{detectedTopic}'");
                    shouldStreamAnswer = true;

                    // Card-start timing is diagnostic only; the UI shows completed-answer time.
                    var cardStartLatencyMs = MillisecondsSince(questionEndTime);

                    // Update context and UI on main thread
                    mainThread.Post(() =>
                    {
                        if (!IsCurrentTurn(generation)) return;
                        DebugLog(LogCategory.Delegate, $"Calling processorDidReceiveQuestion for '{fullText.Prefix(50)}...'");
                        Delegate?.ProcessorShowLoading("💭 Generating answer...", LoadingColor.AppleGreen, turnId);
                        Delegate?.ProcessorDidReceiveQuestion(fullText, detectedTopic, messageType, source, turnId, turnSequence);
                        DebugLog(LogCategory.Delegate, $"Calling processorDidStartStreaming with cardStart={cardStartLatencyMs}ms");
                        Delegate?.ProcessorDidStartStreaming(messageType, detectedTopic, cardStartLatencyMs, turnId, turnSequence);
                    });

                    context.AddUtterance(fullText, detectedTopic, isQuestion: true, sequence: turnSequence);
                    lastAnswerTime = DateTime.UtcNow;
                    PublishBriefing(fullText, true, userBackground);
                }
            }

            void OnAnswerChunk(string chunk)
            {
                if (!IsCurrentTurn(generation)) return;
                if (!shouldStreamAnswer) return;
                if (turnStreamState.AppendVisibleChunk(chunk) is not { } update) return;
                var snapshot = update.Snapshot;
                mainThread.Post(() =>
                {
                    if (!IsCurrentTurn(generation)) return;
                    if (snapshot.Length < 100 || snapshot.Length % 200 == 0)
                    {
                        DebugLog(LogCategory.Stream, $"Chunk received, total: {snapshot.Length} chars");
                    }
                    Delegate?.ProcessorDidReceiveAnswerChunk(StripInlineMarkdownForCueCard(snapshot), turnId);
                });
            }

            var result = await haiku.ClassifyAndStreamAnswerAsync(
                trimmed,
                bufferSnapshot,
                lastTopic,
                userBackground.Length == 0 ? null : userBackground,
                messagesForApi,
                OnClassification,
                OnAnswerChunk,
                jevMaySuppressAnswer: !QuestionSignalFor(Joined(bufferSnapshot, trimmed), lastTopic).ProtectsFromSkip,
                cancellationToken: ct).ConfigureAwait(false);

            DebugLog(LogCategory.Answer, $"classifyAndStreamAnswer returned, shouldStreamAnswer={Flag(shouldStreamAnswer)}");

            if (!IsCurrentTurn(generation) || ct.IsCancellationRequested)
            {
                turnStreamState.Cancel();
                await HideLoading(turnId).ConfigureAwait(false);
                return;
            }

            var totalLatency = (DateTime.UtcNow - startTime).TotalSeconds * 1000;

            if (result.Error is { } failure)
            {
                DebugLog(LogCategory.Error, $"Combined call FAILED: {Describe(failure)}");
                if (shouldStreamAnswer)
                {
                    await OnMain(() => Delegate?.ProcessorDidReceiveAnswerChunk($"Error: {Describe(failure)}", turnId)).ConfigureAwait(false);
                }
            }
            else
            {
                DebugLog(LogCategory.Answer, "Result: SUCCESS");
                if (shouldStreamAnswer)
                {
                    var rawAnswer = turnStreamState.Text;
                    if (rawAnswer.Trim().Length == 0)
                    {
                        // The combined call accepted this as a question but produced no text. Answer it
                        // on the card that is already open instead of leaving an error behind.
                        DebugLog(LogCategory.Answer, "Combined call returned no answer text; recovering with the direct answer stream");
                        var recovered = await StreamDirectHaikuAnswerAsync(
                            fullText, detectedTopic, messageType == MessageType.FollowUp, source, haiku, userBackground, context,
                            pinnedSolution, turnId, turnSequence, generation, questionEndTime, true, ct).ConfigureAwait(false);
                        await OnMain(() =>
                        {
                            if (!recovered) Delegate?.ProcessorDidReceiveAnswerChunk("Error: No answer text received", turnId);
                            Delegate?.ProcessorHideLoading(turnId);
                        }).ConfigureAwait(false);
                        return;
                    }
                    DebugLog(LogCategory.Answer, $"Answer complete ({(int)totalLatency}ms), {rawAnswer.Length} chars");
                    DebugLog(LogCategory.Delegate, "Calling processorDidFinishAnswer");
                    // Total time from question end to fully-streamed answer (not just time-to-card)
                    var answerLatencyMs = MillisecondsSince(questionEndTime);
                    var cleanedAnswer = ConversationalDisplayAnswer(rawAnswer);
                    await OnMain(() =>
                    {
                        if (cleanedAnswer != rawAnswer) Delegate?.ProcessorDidReceiveAnswerChunk(cleanedAnswer, turnId);
                        Delegate?.ProcessorDidFinishAnswer(cleanedAnswer, answerLatencyMs, turnId);
                    }).ConfigureAwait(false);

                    ScheduleSummarizationIfNeeded(context, generation);
                }
            }

            await HideLoading(turnId).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var message = UserFacingProcessingError(error);
            DebugLog(LogCategory.Error, $"Error processing audio: {Describe(error)}");
            await OnMain(() =>
            {
                Delegate?.ProcessorHideLoading(turnId);
                Delegate?.ProcessorDidUpdateStatus(message);
            }).ConfigureAwait(false);
        }
    }

    /// Answers a held question once the interviewer has stayed quiet. A later turn that joins the buffer
    /// takes it over, so a question that does go on is heard as one; one that turns out to be noise
    /// leaves it alone, so waits for turns still in flight rather than giving the question up.
    private void AnswerBufferedQuestionAfterSilence(
        string question,
        string bufferedText,
        string topic,
        CaptureDevice source,
        AnthropicClient haiku,
        ConversationContext context,
        int turnSequence,
        int generationOfBuffer,
        int generation)
    {
        var taskId = Guid.NewGuid();
        StartTurnTask(taskId, async ct =>
        {
            await Pause(AppConstants.Thresholds.BufferedQuestionSilence, ct).ConfigureAwait(false);

            bool IsHeld()
            {
                lock (processingStateLock)
                {
                    return bufferGeneration == generationOfBuffer
                        && utteranceBufferSequence == turnSequence
                        && utteranceBuffer == bufferedText;
                }
            }

            bool OtherTurnsRunning()
            {
                lock (turnLock) return processTasks.Keys.Any(id => id != taskId);
            }

            var deadline = DateTime.UtcNow.AddSeconds(BufferTimeout);
            while (!ct.IsCancellationRequested && IsHeld() && DateTime.UtcNow < deadline && OtherTurnsRunning())
            {
                await Pause(0.25, ct).ConfigureAwait(false);
            }
            if (ct.IsCancellationRequested || !IsCurrentTurn(generation)) return;

            lock (processingStateLock)
            {
                if (bufferGeneration != generationOfBuffer
                    || utteranceBufferSequence != turnSequence
                    || utteranceBuffer != bufferedText)
                {
                    return;
                }
                utteranceBuffer = "";
                bufferTimestamp = null;
                utteranceBufferSequence = null;
            }
            Log.Print($"📦 PROCESS: Answering buffered question after silence: '{question}'");

            var turnId = Guid.NewGuid();
            var background = await mainThread.RunAsync(() => Delegate?.UserBackground ?? "").ConfigureAwait(false);
            var pinned = await mainThread.RunAsync(() => Delegate?.PinnedSolution).ConfigureAwait(false);
            var answered = await StreamDirectHaikuAnswerAsync(
                question, topic, false, source, haiku, background, context, pinned, turnId, turnSequence,
                generation, DateTime.UtcNow, false, ct).ConfigureAwait(false);
            if (answered)
            {
                PublishBriefing(question, true, background);
            }
            else
            {
                DebugLog(LogCategory.Error, $"Buffered question produced no answer: {question}");
            }
            await HideLoading(turnId).ConfigureAwait(false);
        });
    }

    /// Waits, and returns early without throwing when the turn is cancelled.
    private static async Task Pause(double seconds, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void PublishBriefing(string utterance, bool isQuestion, string resumeText)
    {
        BriefingDecision decision;
        lock (processingStateLock)
        {
            decision = Briefing.NextInterviewBriefing(utterance, isQuestion, resumeText, currentBriefing);
            if (decision.Briefing is { } updated) currentBriefing = updated;
        }
        if (decision.Briefing is not { } briefing) return;
        mainThread.Post(() => Delegate?.ProcessorDidUpdateBriefing(briefing));
    }

    private static async Task<(string Text, double LatencyMs)> TranscribeWithTimeoutAsync(
        GroqInterviewClient client, byte[] audioData, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var transcription = Task.Run(() => client.TranscribeAsync(audioData, null, race.Token), CancellationToken.None);
        var timeout = Task.Delay(TimeSpan.FromSeconds(TranscriptionTimeout), race.Token);
        var first = await Task.WhenAny(transcription, timeout).ConfigureAwait(false);
        if (first == transcription)
        {
            race.Cancel();
            return await transcription.ConfigureAwait(false);
        }

        race.Cancel();
        _ = transcription.ContinueWith(static finished => _ = finished.Exception, TaskContinuationOptions.OnlyOnFaulted);
        ct.ThrowIfCancellationRequested();
        throw new TranscriptionTimeoutException(TranscriptionTimeout);
    }

    private static string UserFacingProcessingError(Exception error)
    {
        if (ApiHttp.IsNetworkError(error))
        {
            return "Error: Groq transcription network issue. Check connection and try again.";
        }
        if (error is ApiClientException apiError && apiError.Domain == GroqChat.Domain)
        {
            return $"Error: Groq transcription failed: {error.Message}";
        }
        return $"Error: {Describe(error)}";
    }
}
