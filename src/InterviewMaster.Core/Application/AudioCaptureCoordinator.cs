using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.AppServices;

public sealed class AudioSelectionException : Exception
{
    public AudioSelectionException()
        : base("The microphone is already reserved by Practice or another recording. Stop that recording first.")
    {
    }
}

/// Main-thread controller with a serialized hardware queue. Permission waits do not own that queue.
/// Every member is called on the UI thread, and its `await`s resume there.
public sealed class AudioCaptureCoordinator
{
    private sealed record Slot(Guid Id, IAudioSourceCapture Capture, MicrophoneLease? Lease);

    private readonly IMainThread mainThread;
    private readonly Func<CaptureDevice, IAudioSourceCapture> make;
    private readonly Func<CaptureDevice, bool, Task> preflight;
    private Guid? sessionId;
    private Guid? requestId;
    private ulong epoch;
    private Slot? active;
    private readonly Dictionary<Guid, Slot> owned = new();
    private readonly Dictionary<Guid, MicrophoneLease> permissionLeases = new();
    private readonly Dictionary<Guid, Exception> pendingErrors = new();
    private Task? lifecycleTail;
    private bool terminating;

    public MicrophoneReservations Reservations { get; }
    public CaptureDevice SelectedDevice { get; private set; }
    public bool IsMuted { get; private set; }
    public CaptureDevice? ActiveDevice => active?.Capture.CaptureDevice;
    public bool IsSwitching => requestId is not null;
    public bool SessionActive => sessionId is not null;

    public Action? OnStateChange { get; set; }
    public Action? OnResetBuffers { get; set; }
    public Action<CaptureDevice, bool>? OnCommitted { get; set; }
    public Action<CaptureDevice, Exception>? OnProblem { get; set; }
    public Action<float, bool>? OnLevel { get; set; }
    public Action<byte[], CaptureDevice>? OnPreview { get; set; }
    public Action? OnCancelled { get; set; }
    public Action<byte[], CaptureDevice>? OnSegment { get; set; }
    public Action<string>? OnStatus { get; set; }

    public AudioCaptureCoordinator(
        IMainThread mainThread,
        MicrophoneReservations reservations,
        Func<CaptureDevice, IAudioSourceCapture> make,
        Func<CaptureDevice, bool, Task> preflight,
        CaptureDevice initialDevice = CaptureDevice.SystemAudio)
    {
        this.mainThread = mainThread;
        Reservations = reservations;
        SelectedDevice = initialDevice;
        this.make = make;
        this.preflight = preflight;
    }

    /// Queues work on the UI thread, as `Task { @MainActor in ... }` does; the task ends when the work does.
    private Task StartOnMain(Func<Task> work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mainThread.Post(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception error)
            {
                Log.Print($"Audio capture work failed: {error.Message}");
            }
            finally
            {
                completion.SetResult();
            }
        });
        return completion.Task;
    }

    public void StartSession(CaptureDevice device)
    {
        if (terminating) return;
        StopSession();
        sessionId = Guid.NewGuid();
        SelectedDevice = device;
        BeginRequest(device, false);
    }

    public void Select(CaptureDevice device)
    {
        if (terminating || requestId is not null) return;
        if (ActiveDevice == device) return;
        BeginRequest(device, true);
    }

    private void BeginRequest(CaptureDevice device, bool isExplicit)
    {
        var id = Guid.NewGuid();
        var session = sessionId;
        requestId = id;
        OnStateChange?.Invoke();
        StartOnMain(async () =>
        {
            if (!IsCurrent(id, session)) return;
            try
            {
                // A previous session can still own a mic while its hardware drains.
                if (device == CaptureDevice.Microphone && Reservations.Current?.Owner == MicrophoneOwner.Interview)
                {
                    if (lifecycleTail is { } draining) await draining;
                    if (!IsCurrent(id, session)) return;
                }
                if (device == CaptureDevice.Microphone)
                {
                    permissionLeases[id] = Reservations.Acquire(MicrophoneOwner.Interview) ?? throw new AudioSelectionException();
                }
                await preflight(device, true);
                if (!IsCurrent(id, session))
                {
                    ReleasePermission(id);
                    return;
                }
                if (session is null)
                {
                    SelectedDevice = device;
                    ReleasePermission(id);
                    requestId = null;
                    OnCommitted?.Invoke(device, isExplicit);
                    OnStateChange?.Invoke();
                    return;
                }
                var predecessor = lifecycleTail;
                lifecycleTail = StartOnMain(async () =>
                {
                    if (predecessor is not null) await predecessor;
                    if (!IsCurrent(id, session))
                    {
                        ReleasePermission(id);
                        return;
                    }
                    await PerformSwitch(device, id, session, isExplicit);
                });
            }
            catch (Exception error)
            {
                ReleasePermission(id);
                if (!IsCurrent(id, session)) return;
                requestId = null;
                OnStateChange?.Invoke();
                OnProblem?.Invoke(device, error);
            }
        });
    }

    private async Task PerformSwitch(CaptureDevice device, Guid id, Guid? session, bool isExplicit)
    {
        var previous = active;
        active = null;
        unchecked { epoch++; }
        if (previous is not null) Detach(previous.Capture);
        OnResetBuffers?.Invoke();
        OnStateChange?.Invoke();
        if (previous is not null) await Drain(previous);
        if (!IsCurrent(id, session))
        {
            ReleasePermission(id);
            return;
        }
        try
        {
            await Activate(device, id, session);
            if (!IsCurrent(id, session)) return;
            FinishSelection(device, id, isExplicit);
        }
        catch (Exception originalError)
        {
            ReleasePermission(id);
            if (!IsCurrent(id, session)) return;
            if (previous is not null)
            {
                var fallback = previous.Capture.CaptureDevice;
                try
                {
                    await preflight(fallback, false);
                    if (!IsCurrent(id, session)) return;
                    if (fallback == CaptureDevice.Microphone)
                    {
                        permissionLeases[id] = Reservations.Acquire(MicrophoneOwner.Interview) ?? throw new AudioSelectionException();
                    }
                    await Activate(fallback, id, session);
                    if (!IsCurrent(id, session)) return;
                    FinishSelection(fallback, id, false);
                    OnProblem?.Invoke(device, originalError);
                    return;
                }
                catch (Exception)
                {
                    ReleasePermission(id);
                    if (!IsCurrent(id, session)) return;
                }
            }
            requestId = null;
            OnStateChange?.Invoke();
            OnProblem?.Invoke(device, originalError);
        }
    }

    private async Task Activate(CaptureDevice device, Guid id, Guid? session)
    {
        if (!IsCurrent(id, session)) throw new OperationCanceledException();
        unchecked { epoch++; }
        permissionLeases.Remove(id, out var lease);
        var slot = new Slot(Guid.NewGuid(), make(device), lease);
        owned[slot.Id] = slot;
        InstallCallbacks(slot, session, epoch);
        slot.Capture.SetMuted(IsMuted);
        try
        {
            await slot.Capture.StartCapturingAsync();
            if (pendingErrors.Remove(slot.Id, out var failure)) throw failure;
            if (!IsCurrent(id, session)) throw new OperationCanceledException();
            slot.Capture.SetMuted(IsMuted);
            active = slot;
        }
        catch (Exception)
        {
            await Drain(slot);
            throw;
        }
    }

    private void FinishSelection(CaptureDevice device, Guid id, bool isExplicit)
    {
        if (requestId != id) return;
        SelectedDevice = device;
        requestId = null;
        OnCommitted?.Invoke(device, isExplicit);
        OnStateChange?.Invoke();
    }

    private bool IsCurrent(Guid id, Guid? session) => !terminating && requestId == id && sessionId == session;

    private bool Accepts(Guid slot, Guid? session, ulong generation) =>
        session is not null && sessionId == session && active?.Id == slot && epoch == generation;

    private void InstallCallbacks(Slot slot, Guid? session, ulong generation)
    {
        var device = slot.Capture.CaptureDevice;
        var slotId = slot.Id;
        slot.Capture.OnLevelUpdate = (db, speaking) => mainThread.Post(() =>
        {
            if (!Accepts(slotId, session, generation)) return;
            OnLevel?.Invoke(db, speaking);
        });
        slot.Capture.OnSpeechPreview = data => mainThread.Post(() =>
        {
            if (!Accepts(slotId, session, generation)) return;
            OnPreview?.Invoke(data, device);
        });
        slot.Capture.OnSpeechCancelled = () => mainThread.Post(() =>
        {
            if (!Accepts(slotId, session, generation)) return;
            OnCancelled?.Invoke();
        });
        slot.Capture.OnSpeechSegment = data => mainThread.Post(() =>
        {
            if (!Accepts(slotId, session, generation)) return;
            OnSegment?.Invoke(data, device);
        });
        slot.Capture.OnStatusChange = status => mainThread.Post(() =>
        {
            if (!Accepts(slotId, session, generation)) return;
            OnStatus?.Invoke(status);
        });
        slot.Capture.OnCaptureFailed = error => mainThread.Post(() =>
        {
            if (sessionId != session || epoch != generation || !owned.TryGetValue(slotId, out var failedSlot)) return;
            if (active?.Id != slotId)
            {
                pendingErrors[slotId] = error;
                return;
            }
            active = null;
            unchecked { epoch++; }
            Detach(failedSlot.Capture);
            var predecessor = lifecycleTail;
            lifecycleTail = StartOnMain(async () =>
            {
                if (predecessor is not null) await predecessor;
                await Drain(failedSlot);
            });
            OnStateChange?.Invoke();
            OnProblem?.Invoke(device, error);
        });
    }

    public void SetMuted(bool muted)
    {
        IsMuted = muted;
        foreach (var slot in owned.Values) slot.Capture.SetMuted(muted);
        OnStateChange?.Invoke();
    }

    public void StopSession()
    {
        sessionId = null;
        requestId = null;
        unchecked { epoch++; }
        active = null;
        IsMuted = false;
        foreach (var id in permissionLeases.Keys.ToList()) ReleasePermission(id);
        var slots = owned.Values.ToList();
        foreach (var slot in slots) Detach(slot.Capture);
        var predecessor = lifecycleTail;
        lifecycleTail = StartOnMain(async () =>
        {
            if (predecessor is not null) await predecessor;
            foreach (var slot in slots)
            {
                if (owned.ContainsKey(slot.Id)) await Drain(slot);
            }
        });
        OnStateChange?.Invoke();
    }

    public void Terminate()
    {
        terminating = true;
        StopSession();
        foreach (var slot in owned.Values) slot.Capture.StopCapturingSync();
    }

    public Task WaitForHardwareIdleAsync() => lifecycleTail ?? Task.CompletedTask;

    private async Task Drain(Slot slot)
    {
        Detach(slot.Capture);
        await slot.Capture.StopCapturingAsync();
        owned.Remove(slot.Id);
        pendingErrors.Remove(slot.Id);
        if (slot.Lease is { } lease) Reservations.Release(lease);
    }

    private void ReleasePermission(Guid id)
    {
        if (permissionLeases.Remove(id, out var lease)) Reservations.Release(lease);
    }

    private static void Detach(IAudioSourceCapture capture)
    {
        capture.OnLevelUpdate = null;
        capture.OnSpeechPreview = null;
        capture.OnSpeechCancelled = null;
        capture.OnSpeechSegment = null;
        capture.OnStatusChange = null;
        capture.OnCaptureFailed = null;
    }
}
