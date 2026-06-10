using System.Runtime.InteropServices;
using WindowsAI.Services.Interop;

namespace WindowsAI.Services;

/// <summary>
/// Helpers for a borderless window: dragging by an arbitrary region, resizing from a grip,
/// and computing the work area of the monitor under the cursor (for summon positioning).
/// </summary>
public static class WindowChrome
{
    /// <summary>Begins a move loop as if the user grabbed a title bar (call from PointerPressed).</summary>
    public static void BeginDrag(IntPtr hwnd)
    {
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
    }

    /// <summary>Begins a resize loop from the bottom-right corner (call from a grip's PointerPressed).</summary>
    public static void BeginResizeBottomRight(IntPtr hwnd)
    {
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(hwnd, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTBOTTOMRIGHT, IntPtr.Zero);
    }

    /// <summary>Brings a window to the foreground (needed after showing a hidden overlay).</summary>
    public static void BringToForeground(IntPtr hwnd) => NativeMethods.SetForegroundWindow(hwnd);

    private const uint DWMWA_BORDER_COLOR = 34;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref uint value, int size);

    /// <summary>
    /// Removes the 1px border Windows 11 draws around borderless windows
    /// (SetBorderAndTitleBar(false, false) alone still leaves it).
    /// </summary>
    public static void RemoveWindowBorder(IntPtr hwnd)
    {
        var none = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(uint));
    }

    /// <summary>Work area (excludes taskbar) of the monitor currently under the mouse cursor.</summary>
    public static WorkArea GetCursorWorkArea()
    {
        if (!NativeMethods.GetCursorPos(out var pt))
            return new WorkArea(0, 0, 1920, 1080);

        var monitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return new WorkArea(0, 0, 1920, 1080);

        var r = info.rcWork;
        return new WorkArea(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}

/// <summary>A monitor work area in physical pixels.</summary>
public readonly record struct WorkArea(int X, int Y, int Width, int Height);
