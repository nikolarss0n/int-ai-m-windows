using System.ComponentModel;
using System.Runtime.InteropServices;
using InterviewMaster.AppServices;
using InterviewMaster.Support;

namespace InterviewMaster.Platform.Windows.Input;

/// A system-wide low-level keyboard hook: the Windows counterpart of the macOS event tap. It sees
/// every key before the app in front does, asks the router whether the key is one of the app's
/// shortcuts, and swallows it if so. It needs no permission and no administrator rights.
///
/// Windows drops a hook whose callback is slow, so the callback only reads the latest state, decides,
/// and posts the action to the UI thread.
public sealed class KeyboardHook : IGlobalKeys
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmQuit = 0x0012;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkRightMenu = 0xA5;
    private const uint InjectedFlag = 0x10;

    private readonly ShortcutRouter router = new();
    private readonly IMainThread mainThread;
    private readonly Action<ShortcutAction> perform;
    private readonly HookProc callback;
    private readonly ManualResetEventSlim ready = new(false);
    private volatile ShortcutState state = new();
    private volatile bool observesKeys;
    private Thread? thread;
    private uint threadId;
    private IntPtr hook;
    private Exception? installError;

    public KeyboardHook(IMainThread mainThread, Action<ShortcutAction> perform)
    {
        this.mainThread = mainThread;
        this.perform = perform;
        // Kept in a field: the system holds only a raw pointer to it.
        callback = OnKey;
    }

    /// Published by the UI thread whenever the window, tab, interview or dialogs change.
    public ShortcutState State
    {
        get => state;
        set => state = value;
    }

    /// Told, on the UI thread, about each of the app's keys going down while `ObservesKeys` is on.
    /// The key still reaches the app in front: this is how the floating code is scrolled and moved.
    public Action<KeyStroke>? KeyObserved { get; set; }

    public bool ObservesKeys
    {
        get => observesKeys;
        set => observesKeys = value;
    }

    public bool IsInstalled => hook != IntPtr.Zero;

    /// Throws when Windows refuses the hook; the app then has only its in-window shortcuts.
    public void Install()
    {
        if (thread is not null) return;
        thread = new Thread(Run) { IsBackground = true, Name = "InterviewMaster.KeyboardHook" };
        thread.Start();
        ready.Wait();
        if (installError is not null) throw installError;
    }

    /// Forgets a held mute key when a session ends.
    public void ResetPushToTalk() => router.Reset();

    private void Run()
    {
        threadId = GetCurrentThreadId();
        hook = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) installError = new Win32Exception(Marshal.GetLastWin32Error());
        ready.Set();
        if (hook == IntPtr.Zero) return;

        // The hook is called through this thread's message queue.
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        try
        {
            var message = wParam.ToInt32();
            var isDown = message is WmKeyDown or WmSysKeyDown;
            if (!isDown && message is not (WmKeyUp or WmSysKeyUp)) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            var data = Marshal.PtrToStructure<KeyboardData>(lParam);
            // Keys typed by other programs (remote control, automation) are not the user's shortcuts.
            if ((data.Flags & InjectedFlag) != 0) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);

            var key = Map(data.VirtualKey);
            if (key == ShortcutKey.Other) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
            // The hook is not told about autorepeat. The key's state is updated only after the hook
            // returns, so a key that is already down when it goes down again is repeating.
            var isRepeat = isDown && IsDown((int)data.VirtualKey);

            var stroke = new KeyStroke(
                key, isDown,
                Control: IsDown(VkControl), Shift: IsDown(VkShift), Alt: IsDown(VkMenu),
                IsRepeat: isRepeat, RightAlt: IsDown(VkRightMenu));
            if (isDown && ObservesKeys && KeyObserved is { } observer) mainThread.Post(() => observer(stroke));
            var decision = router.Handle(stroke, state);
            if (decision.Action != ShortcutAction.None)
            {
                var action = decision.Action;
                mainThread.Post(() => perform(action));
            }
            if (decision.Consume) return new IntPtr(1);
        }
        catch (Exception error)
        {
            Log.Print($"⚠️ Keyboard hook: {error.Message}");
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static ShortcutKey Map(uint virtualKey) => virtualKey switch
    {
        0x41 => ShortcutKey.A,
        0x42 => ShortcutKey.B,
        0x43 => ShortcutKey.C,
        0x46 => ShortcutKey.F,
        0x47 => ShortcutKey.G,
        0x49 => ShortcutKey.I,
        0x4A => ShortcutKey.J,
        0x4B => ShortcutKey.K,
        0x4D => ShortcutKey.M,
        0x53 => ShortcutKey.S,
        0x31 => ShortcutKey.Digit1,
        0x32 => ShortcutKey.Digit2,
        0x33 => ShortcutKey.Digit3,
        0x34 => ShortcutKey.Digit4,
        0x0D => ShortcutKey.Enter,
        0x25 => ShortcutKey.Left,
        0x26 => ShortcutKey.Up,
        0x27 => ShortcutKey.Right,
        0x28 => ShortcutKey.Down,
        // The backslash key of a US keyboard, and the extra key beside left Shift on others.
        0xDC or 0xE2 => ShortcutKey.Backslash,
        _ => ShortcutKey.Other
    };

    public void Dispose()
    {
        if (thread is null) return;
        if (threadId != 0) PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
        thread = null;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
