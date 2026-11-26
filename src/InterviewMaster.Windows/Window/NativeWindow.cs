using System.Runtime.InteropServices;
using InterviewMaster.Support;

namespace InterviewMaster.Platform.Windows.Window;

/// What the overlay window needs from Win32 and Avalonia does not offer. Each call takes the
/// window's handle (`TopLevel.TryGetPlatformHandle()`).
public static class NativeWindow
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;
    private const uint WdaNone = 0x0;
    private const uint WdaMonitor = 0x1;
    private const uint WdaExcludeFromCapture = 0x11;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    /// Keeps the window out of screen shares, recordings and screenshots: `sharingType = .none` on
    /// macOS. Windows 10 before version 2004 cannot leave a window out; there it is shown as a black
    /// box instead, and the result says so.
    public static CaptureExclusion SetExcludedFromCapture(IntPtr window, bool excluded)
    {
        if (!excluded)
        {
            SetWindowDisplayAffinity(window, WdaNone);
            return CaptureExclusion.Off;
        }
        if (SetWindowDisplayAffinity(window, WdaExcludeFromCapture)) return CaptureExclusion.Hidden;
        if (SetWindowDisplayAffinity(window, WdaMonitor))
        {
            Log.Print("⚠️ This Windows version cannot hide the window from capture; it shows as a black box.");
            return CaptureExclusion.BlackBox;
        }
        Log.Print($"⚠️ The window could not be kept out of screen capture (error {Marshal.GetLastWin32Error()}).");
        return CaptureExclusion.Off;
    }

    /// Clicks pass through to the app behind: `ignoresMouseEvents` on macOS.
    public static void SetClickThrough(IntPtr window, bool enabled) =>
        SetExtendedStyle(window, WsExTransparent | WsExLayered, enabled, keep: WsExLayered);

    /// The window never takes the keyboard from the app in front, so a browser never sees it lose
    /// focus. Practice turns this off while the learner types.
    public static void SetNeverActivates(IntPtr window, bool enabled) =>
        SetExtendedStyle(window, WsExNoActivate, enabled);

    /// Out of the taskbar and Alt+Tab, as an accessory app is out of the Dock.
    public static void SetHiddenFromTaskSwitcher(IntPtr window, bool hidden) =>
        SetExtendedStyle(window, WsExToolWindow, hidden);

    private static void SetExtendedStyle(IntPtr window, long bits, bool enabled, long keep = 0)
    {
        var current = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        var next = enabled ? current | bits : current & ~(bits & ~keep);
        if (next == current) return;
        SetWindowLongPtr(window, GwlExStyle, new IntPtr(next));
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

public enum CaptureExclusion
{
    Off,
    Hidden,
    BlackBox
}
