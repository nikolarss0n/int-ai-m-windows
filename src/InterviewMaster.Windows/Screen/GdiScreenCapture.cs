using System.ComponentModel;
using System.Runtime.InteropServices;
using InterviewMaster.Domain;
using InterviewMaster.Infrastructure;

namespace InterviewMaster.Platform.Windows.Screen;

/// Copies the primary display with GDI. The app's own window is left out because it is marked
/// excluded from capture (see `NativeWindow.SetExcludedFromCapture`); ScreenCaptureKit is told the
/// same thing explicitly on macOS.
public sealed class GdiScreenCapture : IScreenCapture
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int SrcCopy = 0x00CC0020;

    public Task<Screenshot> CaptureScreenAsync() => Task.Run(Capture);

    private static Screenshot Capture()
    {
        var width = GetSystemMetrics(SmCxScreen);
        var height = GetSystemMetrics(SmCyScreen);
        if (width <= 0 || height <= 0)
        {
            throw new ScreenCaptureException(ScreenCaptureFailure.NoDisplayFound, "No display found");
        }

        var screen = IntPtr.Zero;
        var memory = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw Failed("The screen could not be opened");
            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) throw Failed("The screen could not be copied");
            var info = new BitmapInfo
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>(),
                Width = width,
                // Negative: rows from the top.
                Height = -height,
                Planes = 1,
                BitCount = 32
            };
            bitmap = CreateDIBSection(memory, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) throw Failed("The screen could not be copied");
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, 0, 0, SrcCopy)) throw Failed("The screen could not be copied");

            var stride = width * 4;
            var pixels = new byte[stride * height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new Screenshot(PngEncoder.EncodeBgra(pixels, width, height, stride), width, height);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static ScreenCaptureException Failed(string what) =>
        new(ScreenCaptureFailure.CaptureFailed, $"{what} ({new Win32Exception(Marshal.GetLastWin32Error()).Message})");

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr context);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr context);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr context, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr context, IntPtr value);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr context);
}
