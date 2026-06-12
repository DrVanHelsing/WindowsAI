using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WindowsAI_App.Helpers;

/// <summary>
/// Produces the real Windows shell icon/thumbnail for a file, folder, or app
/// (<c>shell:AppsFolder\{AUMID}</c>) as a WinUI <see cref="ImageSource"/> via
/// <c>IShellItemImageFactory</c> — the same images Explorer and the Start menu show. One uniform
/// path covers all three because app hits are passed as shell parsing names. Results are cached
/// per target+size.
/// </summary>
/// <remarks>
/// Call (and await) on the UI thread: the shell COM work runs on a thread-pool thread, but the
/// resulting <see cref="WriteableBitmap"/> is built on the awaiting (UI) thread.
/// </remarks>
public static class ShellIconProvider
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();

    public static async Task<ImageSource?> GetIconAsync(string target, int pixelSize)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;

        var key = $"{target}|{pixelSize}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var captured = await Task.Run(() => CapturePixels(target, pixelSize)).ConfigureAwait(true);
        if (captured is not var (data, w, h) || data is null)
        {
            Cache[key] = null;
            return null;
        }

        var bmp = new WriteableBitmap(w, h);
        using (var stream = bmp.PixelBuffer.AsStream())
            stream.Write(data, 0, data.Length);
        bmp.Invalidate();

        Cache[key] = bmp;
        return bmp;
    }

    /// <summary>Runs the shell COM + GDI copy off the UI thread; returns premultiplied BGRA pixels.</summary>
    private static (byte[] data, int w, int h)? CapturePixels(string target, int pixelSize)
    {
        object? shellItem = null;
        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            var iid = IID_IShellItemImageFactory;
            SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out shellItem);
            if (shellItem is not IShellItemImageFactory factory)
                return null;

            // No ICONONLY flag → a thumbnail where the shell has one (image previews, doc pages),
            // falling back to the file-type / app icon. BIGGERSIZEOK lets it upscale to our box.
            var size = new SIZE { cx = pixelSize, cy = pixelSize };
            factory.GetImage(size, SIIGBF.BiggerSizeOk, out hbitmap);
            if (hbitmap == IntPtr.Zero)
                return null;

            if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0 ||
                bm.bmBits == IntPtr.Zero || bm.bmBitsPixel != 32 || bm.bmWidth <= 0 || bm.bmHeight <= 0)
                return null;

            int w = bm.bmWidth, h = bm.bmHeight, stride = bm.bmWidthBytes;
            var data = new byte[w * h * 4];
            // Shell DIB section is top-down 32bpp premultiplied BGRA — copy row by row honoring stride.
            for (int y = 0; y < h; y++)
                Marshal.Copy(bm.bmBits + y * stride, data, y * w * 4, w * 4);

            return (data, w, h);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero)
                DeleteObject(hbitmap);
            if (shellItem is not null)
                Marshal.ReleaseComObject(shellItem);
        }
    }

    // ── Interop ──────────────────────────────────────────────────────────────

    private static Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr handle, int c, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);
}
