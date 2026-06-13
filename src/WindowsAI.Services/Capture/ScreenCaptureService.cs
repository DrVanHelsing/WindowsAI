using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WindowsAI.Services.Interop;

namespace WindowsAI.Services.Capture;

/// <summary>A monitor's pixel rectangle plus its DPI scale factor.</summary>
public readonly record struct CaptureBounds(int X, int Y, int Width, int Height, double Scale);

/// <summary>
/// Grabs screen pixels for the visual-intelligence "ask about this region" flow. Captures the
/// whole monitor under the cursor up front into a frozen bitmap so the selection UI can draw it
/// (and the user selects against a still image, never against our own dimming overlay), then crops
/// the chosen sub-region to a PNG to send to the vision model.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenCaptureService
{
    /// <summary>Captures the entire monitor currently under the mouse cursor.</summary>
    public CapturedScreen CaptureMonitorUnderCursor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);

        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0
            ? dpiX / 96.0
            : 1.0;

        var r = info.rcMonitor;
        var bounds = new CaptureBounds(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, scale);

        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
        }

        return new CapturedScreen(bitmap, bounds);
    }
}

/// <summary>
/// A frozen, full-monitor screenshot. Exposes a PNG of the whole capture (for the selection UI to
/// display) and crops sub-regions on demand. Dispose to release the underlying bitmap.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CapturedScreen : IDisposable
{
    private readonly Bitmap _bitmap;

    internal CapturedScreen(Bitmap bitmap, CaptureBounds bounds)
    {
        _bitmap = bitmap;
        Bounds = bounds;
        FullImagePath = Save(bitmap, "screen");
    }

    /// <summary>Physical pixel rectangle of the captured monitor, plus its DPI scale.</summary>
    public CaptureBounds Bounds { get; }

    /// <summary>Temp-file PNG of the whole monitor capture (1:1 with <see cref="Bounds"/>).</summary>
    public string FullImagePath { get; }

    /// <summary>
    /// Crops a sub-region — given in pixels relative to the capture's top-left — to a new PNG and
    /// returns its path. The rectangle is clamped to the captured area.
    /// </summary>
    public string SaveCrop(int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, _bitmap.Width - 1);
        y = Math.Clamp(y, 0, _bitmap.Height - 1);
        width = Math.Clamp(width, 1, _bitmap.Width - x);
        height = Math.Clamp(height, 1, _bitmap.Height - y);

        using var crop = _bitmap.Clone(new Rectangle(x, y, width, height), _bitmap.PixelFormat);
        return Save(crop, "region");
    }

    private static string Save(Bitmap bitmap, string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), "WindowsAI", "captures");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    public void Dispose() => _bitmap.Dispose();
}
