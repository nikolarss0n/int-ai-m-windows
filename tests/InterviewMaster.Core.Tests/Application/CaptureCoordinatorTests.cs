using InterviewMaster.AppServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;
using InterviewMaster.Support;

namespace InterviewMaster.Tests;

/// Port of the lifecycle half of Tests/test_audio_source.swift. Each test body runs on the stand-in UI
/// thread, as the macOS tests run on the main actor.
public sealed class CaptureCoordinatorTests : SwiftStyleTests
{
    private sealed class Gate
    {
        private readonly TaskCompletionSource open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Entered { get; private set; }

        public Task Wait()
        {
            Entered = true;
            return open.Task;
        }

        public void Release() => open.TrySetResult();
    }

    private sealed class TestError : Exception
    {
    }

    private sealed class FakeCapture(CaptureDevice device) : IAudioSourceCapture
    {
        public CaptureDevice CaptureDevice { get; } = device;
        public Action<float, bool>? OnLevelUpdate { get; set; }
        public Action<byte[]>? OnSpeechPreview { get; set; }
        public Action? OnSpeechCancelled { get; set; }
        public Action<byte[]>? OnSpeechSegment { get; set; }
        public Action<string>? OnStatusChange { get; set; }
        public Action<Exception>? OnCaptureFailed { get; set; }
        public Gate? StartGate { get; set; }
        public Gate? StopGate { get; set; }
        public bool Fails { get; set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int SyncStops { get; private set; }
        public bool Muted { get; private set; }
        public bool Running { get; private set; }

        public async Task StartCapturingAsync()
        {
            Starts++;
            if (StartGate is { } gate) await gate.Wait();
            if (Fails) throw new TestError();
            Running = true;
        }

        public async Task StopCapturingAsync()
        {
            Stops++;
            if (StopGate is { } gate) await gate.Wait();
            Running = false;
        }

        public void StopCapturingSync()
        {
            SyncStops++;
            Running = false;
        }

        public void SetMuted(bool muted) => Muted = muted;
    }

    private sealed class Rig
    {
        public MicrophoneReservations Reservations { get; } = new();
        public List<FakeCapture> Captures { get; } = new();
        public Func<CaptureDevice, bool, Task>? Permission { get; set; }
        public Action<FakeCapture, int>? Configure { get; set; }
        public List<(CaptureDevice Device, bool Explicit)> Commits { get; } = new();
        public int Errors { get; private set; }
        public int Resets { get; private set; }
        public List<CaptureDevice> Segments { get; } = new();
        public AudioCaptureCoordinator Coordinator { get; }

        public Rig(IMainThread mainThread)
        {
            Coordinator = new AudioCaptureCoordinator(
                mainThread,
                Reservations,
                device =>
                {
                    var capture = new FakeCapture(device);
                    Configure?.Invoke(capture, Captures.Count);
                    Captures.Add(capture);
                    return capture;
                },
                (device, prompt) => Permission?.Invoke(device, prompt) ?? Task.CompletedTask)
            {
                OnCommitted = (device, isExplicit) => Commits.Add((device, isExplicit)),
                OnProblem = (_, _) => Errors++,
                OnResetBuffers = () => Resets++,
                OnSegment = (_, source) => Segments.Add(source)
            };
        }
    }

    private readonly QueueMainThread main = new();

    public override void Dispose()
    {
        main.Dispose();
        base.Dispose();
    }

    private Task OnMain(Func<Task> body)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Post(async () =>
        {
            try
            {
                await body();
                done.SetResult();
            }
            catch (Exception error)
            {
                done.SetException(error);
            }
        });
        return done.Task.WaitAsync(Harness.Limit);
    }

    private async Task Ticks(int count = 8)
    {
        for (var tick = 0; tick < count; tick++) await main.RunAsync(() => { });
    }

    private async Task Until(string message, Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            if (predicate()) return;
            await Ticks(1);
        }
        throw new Xunit.Sdk.XunitException($"Unresolved async test event: {message}");
    }

    /// Stop can invalidate a request before its work gets its first turn on the UI thread.
    [Fact]
    public Task StopBeforeTheRequestRuns() => OnMain(async () =>
    {
        var rig = new Rig(main);
        var micPermissionRequests = 0;
        rig.Permission = (device, _) =>
        {
            if (device == CaptureDevice.Microphone) micPermissionRequests++;
            return Task.CompletedTask;
        };
        rig.Coordinator.StartSession(CaptureDevice.Microphone);
        rig.Coordinator.StopSession();
        rig.Coordinator.StartSession(CaptureDevice.SystemAudio);
        await Until("synchronous stop/restart", () => rig.Coordinator.ActiveDevice == CaptureDevice.SystemAudio);
        Check(micPermissionRequests == 0 && rig.Reservations.Current is null, "invalidated task must not request permission or reserve mic");
        Check(rig.Captures.Count == 1 && rig.Errors == 0, "only the current task can start hardware");
        rig.Coordinator.StopSession();
        await rig.Coordinator.WaitForHardwareIdleAsync();
    });

    /// A stale permission result cannot switch, alert, persist, or clear a newer request.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StalePermissionResultHasNoEffect(bool denied) => OnMain(async () =>
    {
        var rig = new Rig(main);
        var gate = new Gate();
        rig.Permission = async (device, _) =>
        {
            if (device != CaptureDevice.Microphone) return;
            await gate.Wait();
            if (denied) throw new TestError();
        };
        rig.Coordinator.StartSession(CaptureDevice.SystemAudio);
        await Until("initial source", () => rig.Coordinator.ActiveDevice == CaptureDevice.SystemAudio);
        rig.Coordinator.Select(CaptureDevice.Microphone);
        await Until("pending permission", () => gate.Entered);
        Check(rig.Captures[0].Running, "preflight keeps old source live");
        rig.Coordinator.StopSession();
        rig.Coordinator.StartSession(CaptureDevice.SystemAudio);
        await Until("new session source", () => rig.Commits.Count == 2);
        var resets = rig.Resets;
        gate.Release();
        await Ticks();
        Check(rig.Coordinator.ActiveDevice == CaptureDevice.SystemAudio && rig.Resets == resets, "stale preflight cannot reset new session");
        Check(rig.Errors == 0 && rig.Commits.Count == 2 && !rig.Coordinator.IsSwitching, "stale result has no UI/persistence side effects");
        Check(rig.Reservations.Current is null, "stale permission lease released");
        rig.Coordinator.StopSession();
        await rig.Coordinator.WaitForHardwareIdleAsync();
    });

    /// Late successful AND failed starts must drain before the next hardware start.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LateStartDrainsBeforeTheNextStart(bool fails) => OnMain(async () =>
    {
        var rig = new Rig(main);
        var start = new Gate();
        var stop = new Gate();
        rig.Configure = (capture, index) =>
        {
            if (index != 0) return;
            capture.StartGate = start;
            capture.StopGate = stop;
            capture.Fails = fails;
        };
        rig.Coordinator.StartSession(CaptureDevice.Microphone);
        await Until("old start suspended", () => start.Entered);
        rig.Coordinator.StopSession();
        rig.Coordinator.StartSession(CaptureDevice.SystemAudio);
        await Ticks();
        Check(rig.Captures.Count == 1, "new hardware cannot overlap pending startup");
        start.Release();
        await Until("old stop suspended", () => stop.Entered);
        Check(rig.Captures.Count == 1, "new hardware waits for confirmed stop");
        Check(rig.Reservations.Current?.Owner == MicrophoneOwner.Interview, "mic lease held throughout teardown");
        stop.Release();
        await Until("new source starts after drain", () => rig.Coordinator.ActiveDevice == CaptureDevice.SystemAudio);
        Check(rig.Errors == 0 && rig.Captures.Count == 2, "stale failure never recovers or alerts");
        Check(!rig.Captures[0].Running && rig.Reservations.Current is null, "old engine drained and lease released");
        rig.Coordinator.StopSession();
        await rig.Coordinator.WaitForHardwareIdleAsync();
    });

    /// Already queued callback from the SAME device in an older session is rejected.
    [Fact]
    public Task OldSessionCallbacksAreDropped() => OnMain(async () =>
    {
        var rig = new Rig(main);
        rig.Coordinator.StartSession(CaptureDevice.SystemAudio);
        await Until("callback source", () => rig.Coordinator.ActiveDevice is not null);
        var old = rig.Captures[0].OnSpeechSegment;
        var oldError = rig.Captures[0].OnCaptureFailed;
        rig.Coordinator.StopSession();
        rig.Coordinator.StartSession(CaptureDevice.SystemAudio);
        await Until("replacement source", () => rig.Captures.Count == 2 && rig.Coordinator.ActiveDevice is not null);
        old?.Invoke(new byte[] { 1 });
        oldError?.Invoke(new TestError());
        rig.Captures[1].OnSpeechSegment?.Invoke(new byte[] { 2 });
        await Ticks();
        Check(rig.Segments.SequenceEqual(new[] { CaptureDevice.SystemAudio }) && rig.Errors == 0, "old callbacks dropped even for same device");
        rig.Coordinator.StopSession();
        await rig.Coordinator.WaitForHardwareIdleAsync();
    });

    /// Muting a pending start survives its promotion.
    [Fact]
    public Task MuteSurvivesAPendingStart() => OnMain(async () =>
    {
        var rig = new Rig(main);
        var gate = new Gate();
        rig.Configure = (capture, _) => capture.StartGate = gate;
        rig.Coordinator.StartSession(CaptureDevice.Microphone);
        await Until("mic start pending", () => gate.Entered);
        rig.Coordinator.SetMuted(true);
        Check(rig.Captures[0].Muted, "pending capture receives mute immediately");
        gate.Release();
        await Until("muted capture active", () => rig.Coordinator.ActiveDevice == CaptureDevice.Microphone);
        Check(rig.Captures[0].Muted, "latest mute applied at activation");
        rig.Coordinator.Terminate();
        Check(rig.Captures[0].SyncStops == 1, "quit stops owned mic synchronously");
        await rig.Coordinator.WaitForHardwareIdleAsync();
    });

    /// A current pending failure cannot go live.
    [Fact]
    public Task FailedPendingStartIsNeverPromoted() => OnMain(async () =>
    {
        var rig = new Rig(main);
        var gate = new Gate();
        rig.Configure = (capture, _) => capture.StartGate = gate;
        rig.Coordinator.StartSession(CaptureDevice.Microphone);
        await Until("pending failure", () => gate.Entered);
        rig.Captures[0].OnCaptureFailed?.Invoke(new TestError());
        await Ticks();
        gate.Release();
        await Until("failed pending start drained", () => !rig.Coordinator.IsSwitching);
        Check(rig.Coordinator.ActiveDevice is null && rig.Errors == 1, "failed pending source never promoted");
        Check(rig.Captures.Count == 1, "initial failure has no invented fallback");
    });

    /// Recovery is bounded, restores the previous source, and never persists automatically.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RecoveryIsBounded(bool recoveryFails) => OnMain(async () =>
    {
        var rig = new Rig(main);
        rig.Configure = (capture, index) => capture.Fails = index == 1 || (index == 2 && recoveryFails);
        rig.Coordinator.StartSession(CaptureDevice.Microphone);
        await Until("original mic", () => rig.Coordinator.ActiveDevice == CaptureDevice.Microphone);
        rig.Coordinator.Select(CaptureDevice.SystemAudio);
        await Until("bounded fallback completes", () => !rig.Coordinator.IsSwitching);
        Check(rig.Captures.Select(capture => capture.CaptureDevice).SequenceEqual(new[] { CaptureDevice.Microphone, CaptureDevice.SystemAudio, CaptureDevice.Microphone }), "one fallback to previously working mic");
        Check(rig.Coordinator.ActiveDevice == (recoveryFails ? null : CaptureDevice.Microphone), "recovery state matches actual capture");
        Check(rig.Commits.All(commit => !commit.Explicit), "automatic recovery never overwrites preference");
        Check(rig.Errors == 1, "one failure surfaced");
        rig.Coordinator.StopSession();
        await rig.Coordinator.WaitForHardwareIdleAsync();
    });

    /// Practice pending/running/stopping ownership all use the same lease.
    [Fact]
    public Task PracticeAndInterviewShareOneMicrophoneLease() => OnMain(async () =>
    {
        var rig = new Rig(main);
        var practice = rig.Reservations.Acquire(MicrophoneOwner.Practice)!;
        rig.Coordinator.StartSession(CaptureDevice.SystemAudio);
        await Until("remote plus Practice", () => rig.Coordinator.ActiveDevice == CaptureDevice.SystemAudio);
        rig.Coordinator.Select(CaptureDevice.Microphone);
        await Until("mic denied by Practice", () => !rig.Coordinator.IsSwitching);
        Check(rig.Errors == 1 && rig.Coordinator.ActiveDevice == CaptureDevice.SystemAudio, "Practice busy leaves remote source intact");
        Check(rig.Reservations.Owns(practice), "failed switch cannot release Practice lease");
        rig.Reservations.Release(practice);
        rig.Coordinator.Select(CaptureDevice.Microphone);
        await Until("interview now owns mic", () => rig.Coordinator.ActiveDevice == CaptureDevice.Microphone);
        Check(rig.Reservations.Acquire(MicrophoneOwner.Practice) is null, "reverse contention blocks Practice");
        Check(rig.Commits.Count > 0 && rig.Commits[^1].Explicit, "successful explicit selection persists");
        rig.Coordinator.StopSession();
        await rig.Coordinator.WaitForHardwareIdleAsync();
        Check(rig.Reservations.Current is null, "stop releases microphone");
    });
}
