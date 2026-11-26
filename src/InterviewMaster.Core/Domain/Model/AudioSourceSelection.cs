namespace InterviewMaster.Domain;

public enum MicrophoneOwner
{
    Interview,
    Practice
}

public sealed record MicrophoneLease(MicrophoneOwner Owner)
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// Application-level ownership covers permission requests as well as running recorders.
/// Call it from the UI thread only, as the macOS app calls it from the main queue.
public sealed class MicrophoneReservations
{
    public MicrophoneLease? Current { get; private set; }

    public MicrophoneLease? Acquire(MicrophoneOwner owner)
    {
        if (Current is not null) return null;
        var lease = new MicrophoneLease(owner);
        Current = lease;
        return lease;
    }

    public bool Owns(MicrophoneLease lease) => Current == lease;

    public void Release(MicrophoneLease lease)
    {
        if (Current == lease) Current = null;
    }
}

public sealed class PushToTalkState
{
    private bool clearsLatch;

    public bool Latched { get; private set; }
    public double? PressedAt { get; private set; }
    public Guid? PressId { get; private set; }
    public bool IsMuted => Latched || (PressedAt is not null && !clearsLatch);

    public Guid? KeyDown(double time, bool isRepeat)
    {
        if (isRepeat || PressedAt is not null) return null;
        clearsLatch = Latched;
        if (clearsLatch) Latched = false;
        PressedAt = time;
        var id = Guid.NewGuid();
        PressId = id;
        return id;
    }

    public bool KeyUp(double time)
    {
        if (PressedAt is not { } start) return false;
        if (!clearsLatch) Latched = time - start < 0.4;
        PressedAt = null;
        PressId = null;
        clearsLatch = false;
        return true;
    }

    public void ToggleLatch()
    {
        // A click explicitly replaces the current gesture; its later release is ignored.
        var next = !IsMuted;
        Reset();
        Latched = next;
    }

    public bool Expire(Guid id)
    {
        if (PressId != id) return false;
        Reset();
        return true;
    }

    private void Reset()
    {
        Latched = false;
        PressedAt = null;
        PressId = null;
        clearsLatch = false;
    }
}

public enum CapturePresentationKind
{
    Idle,
    Switching,
    Unavailable,
    Listening,
    Speaking,
    Muted
}

public readonly record struct CapturePresentation(CapturePresentationKind Kind, CaptureDevice? Device = null)
{
    public static readonly CapturePresentation Idle = new(CapturePresentationKind.Idle);
    public static readonly CapturePresentation Switching = new(CapturePresentationKind.Switching);
    public static readonly CapturePresentation Unavailable = new(CapturePresentationKind.Unavailable);
    public static readonly CapturePresentation Muted = new(CapturePresentationKind.Muted);

    public static CapturePresentation Listening(CaptureDevice device) => new(CapturePresentationKind.Listening, device);

    public static CapturePresentation Speaking(CaptureDevice device) => new(CapturePresentationKind.Speaking, device);

    /// The macOS symbol name. The interface maps it to the Windows glyph.
    public string Symbol => Kind switch
    {
        CapturePresentationKind.Idle or CapturePresentationKind.Unavailable => "waveform.slash",
        CapturePresentationKind.Switching => "arrow.triangle.2.circlepath",
        // Not the source's own glyph: the source buttons sit right beside this one.
        CapturePresentationKind.Listening => "ear",
        CapturePresentationKind.Speaking => "waveform",
        _ => "mic.slash.fill"
    };

    public string Label => Kind switch
    {
        CapturePresentationKind.Idle => "Audio starts with the interview",
        CapturePresentationKind.Switching => "Switching audio source",
        CapturePresentationKind.Unavailable => "Audio unavailable. Select a source to try again.",
        CapturePresentationKind.Listening => $"Listening to {Device!.Value.DisplayName()}. Click or press {PlatformCopy.MuteShortcut} to mute.",
        CapturePresentationKind.Speaking => $"Speech detected on {Device!.Value.DisplayName()}. Click or press {PlatformCopy.MuteShortcut} to mute.",
        _ => $"Muted. Click or press {PlatformCopy.MuteShortcut} to unmute."
    };
}

public static class AudioSourceSelection
{
    public static CapturePresentation CapturePresentation(CaptureDevice? active, bool sessionActive, bool switching, bool muted, bool speaking)
    {
        if (!sessionActive) return Domain.CapturePresentation.Idle;
        if (muted) return Domain.CapturePresentation.Muted;
        if (active is { } device) return speaking ? Domain.CapturePresentation.Speaking(device) : Domain.CapturePresentation.Listening(device);
        return switching ? Domain.CapturePresentation.Switching : Domain.CapturePresentation.Unavailable;
    }

    public static CaptureDevice ResolveStartupCaptureDevice(CaptureDevice preferred, bool microphoneAllowed) =>
        preferred == CaptureDevice.Microphone && !microphoneAllowed ? CaptureDevice.SystemAudio : preferred;
}
