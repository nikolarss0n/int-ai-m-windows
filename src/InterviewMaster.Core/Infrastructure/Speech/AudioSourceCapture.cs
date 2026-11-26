using InterviewMaster.Domain;
using InterviewMaster.Support;

namespace InterviewMaster.Infrastructure;

/// One audio input: the microphone or what the computer is playing.
public interface IAudioSourceCapture
{
    CaptureDevice CaptureDevice { get; }
    Action<float, bool>? OnLevelUpdate { get; set; }
    Action<byte[]>? OnSpeechPreview { get; set; }
    Action? OnSpeechCancelled { get; set; }
    Action<byte[]>? OnSpeechSegment { get; set; }
    Action<string>? OnStatusChange { get; set; }
    Action<Exception>? OnCaptureFailed { get; set; }
    Task StartCapturingAsync();
    Task StopCapturingAsync();
    void StopCapturingSync();
    void SetMuted(bool muted);
}

/// Property access and callback snapshots may occur on different audio/main queues.
public class AudioCaptureCallbacks
{
    private readonly object callbackLock = new();
    private readonly IMainThread mainThread;
    private Action<float, bool>? level;
    private Action<byte[]>? preview;
    private Action? cancelled;
    private Action<byte[]>? segment;
    private Action<string>? status;
    private Action<Exception>? failed;

    public AudioCaptureCallbacks(IMainThread mainThread)
    {
        this.mainThread = mainThread;
    }

    protected IMainThread MainThread => mainThread;

    public Action<float, bool>? OnLevelUpdate
    {
        get { lock (callbackLock) return level; }
        set { lock (callbackLock) level = value; }
    }

    public Action<byte[]>? OnSpeechPreview
    {
        get { lock (callbackLock) return preview; }
        set { lock (callbackLock) preview = value; }
    }

    public Action? OnSpeechCancelled
    {
        get { lock (callbackLock) return cancelled; }
        set { lock (callbackLock) cancelled = value; }
    }

    public Action<byte[]>? OnSpeechSegment
    {
        get { lock (callbackLock) return segment; }
        set { lock (callbackLock) segment = value; }
    }

    public Action<string>? OnStatusChange
    {
        get { lock (callbackLock) return status; }
        set { lock (callbackLock) status = value; }
    }

    public Action<Exception>? OnCaptureFailed
    {
        get { lock (callbackLock) return failed; }
        set { lock (callbackLock) failed = value; }
    }

    public void DispatchTurn(SpeechTurnEmit emit, byte[]? audio)
    {
        var onPreview = OnSpeechPreview;
        var onCancelled = OnSpeechCancelled;
        var onSegment = OnSpeechSegment;
        mainThread.Post(() =>
        {
            switch (SpeechTurnPolicy.Action(emit))
            {
                case SpeechTurnAction.PrefetchTranscriptionOnly:
                    if (audio is not null) onPreview?.Invoke(audio);
                    break;
                case SpeechTurnAction.CancelInFlightWork:
                    onCancelled?.Invoke();
                    break;
                case SpeechTurnAction.CommitAnswer:
                    if (audio is not null) onSegment?.Invoke(audio);
                    break;
            }
        });
    }

    public void DispatchLevel(float db, bool speaking)
    {
        var callback = OnLevelUpdate;
        mainThread.Post(() => callback?.Invoke(db, speaking));
    }

    public void DispatchStatus(string text)
    {
        var callback = OnStatusChange;
        mainThread.Post(() => callback?.Invoke(text));
    }

    public void DispatchFailure(Exception error)
    {
        var callback = OnCaptureFailed;
        mainThread.Post(() => callback?.Invoke(error));
    }
}
