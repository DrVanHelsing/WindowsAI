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

    // DWMWA_WINDOW_CORNER_PREFERENCE (33): Win11 rounds window corners by default, which
    // leaves a faint light arc against our square transparent panel. Force square corners.
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const uint DWMWCP_DONOTROUND = 1;
    private const uint DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref uint value, int size);

    /// <summary>
    /// Tints the native Win11 window border (DWM-drawn) to an RGB color so the window edge
    /// follows the OS accent. The window keeps its native frame + rounded corners; only the
    /// 1px border line is recolored. Call again to re-tint when the accent changes.
    /// </summary>
    public static void SetBorderColor(IntPtr hwnd, byte r, byte g, byte b)
    {
        // DWMWA_BORDER_COLOR takes a COLORREF: 0x00BBGGRR.
        var colorref = (uint)(r | (g << 8) | (b << 16));
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorref, sizeof(uint));
    }

    /// <summary>
    /// Gives a borderless, transparent-backdrop window the native Win11 treatment: rounded
    /// corners, the system drop shadow, and a 1px DWM border tinted to (<paramref name="r"/>,
    /// <paramref name="g"/>, <paramref name="b"/>) so the edge reads as a deliberate, app-matching
    /// hairline instead of the default light frame. Use for floating cards / panels whose content
    /// fills the window; pair with a matching content corner radius so the fill hugs the rounding.
    /// </summary>
    public static void RoundWithBorder(IntPtr hwnd, byte r, byte g, byte b)
    {
        var round = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(uint));

        var colorref = (uint)(r | (g << 8) | (b << 16));
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorref, sizeof(uint));
    }

    /// <summary>
    /// Removes the chrome Windows 11 draws around a borderless window: the 1px light
    /// border (SetBorderAndTitleBar(false, false) alone still leaves it) and the rounded
    /// corners (whose anti-aliased arc otherwise shows as a white edge on our panel).
    /// </summary>
    public static void RemoveWindowBorder(IntPtr hwnd)
    {
        var none = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(uint));

        var square = DWMWCP_DONOTROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(uint));
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
