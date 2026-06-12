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
            // Don't cache the miss: a Store-app icon the shell hasn't extracted yet can fail
            // transiently (E_PENDING), so a later open should retry rather than be stuck on the glyph.
            return null;
        }

        var bmp = new WriteableBitmap(w, h);
        using (var stream = bmp.PixelBuffer.AsStream())
            stream.Write(data, 0, data.Length);
        bmp.Invalidate();

        Cache[key] = bmp;
        return bmp;
    }

    /// <summary>Runs the shell COM + GDI copy off the UI thread; returns premultiplied top-down BGRA pixels.</summary>
    private static (byte[] data, int w, int h)? CapturePixels(string target, int pixelSize)
    {
        object? shellItem = null;
        IntPtr hbitmap = IntPtr.Zero;
        IntPtr hdc = IntPtr.Zero;
        try
        {
            var iid = IID_IShellItemImageFactory;
            SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out shellItem);
            if (shellItem is not IShellItemImageFactory factory)
                return null;

            // No ICONONLY flag → a thumbnail where the shell has one (image previews, doc pages),
            // falling back to the file-type / app icon. BIGGERSIZEOK lets the shell hand back a
            // larger native asset (e.g. the 256px icon) that we downsample crisply for hi-DPI.
            var size = new SIZE { cx = pixelSize, cy = pixelSize };

            // Store-app icons the shell hasn't extracted yet return E_PENDING on the first call —
            // the documented contract is to retry. Without this, packaged apps (e.g. WhatsApp) get
            // stuck on the placeholder glyph the very first time they're shown.
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    factory.GetImage(size, SIIGBF.BiggerSizeOk, out hbitmap);
                    break;
                }
                catch (COMException ex) when (ex.HResult == E_PENDING && attempt < 8)
                {
                    System.Threading.Thread.Sleep(60);
                }
            }
            if (hbitmap == IntPtr.Zero)
                return null;

            if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0 ||
                bm.bmBitsPixel != 32 || bm.bmWidth <= 0 || bm.bmHeight <= 0)
                return null;

            int w = bm.bmWidth, h = bm.bmHeight;

            // Don't trust the HBITMAP's in-memory row order (GetImage may hand back a bottom-up DIB,
            // which copied straight reads upside-down). Ask GDI for the pixels with a *negative*
            // height so they always come back top-down 32bpp BGRA, regardless of the source layout.
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // negative → top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };

            var data = new byte[w * h * 4];
            hdc = CreateCompatibleDC(IntPtr.Zero);
            if (GetDIBits(hdc, hbitmap, 0, (uint)h, data, ref header, 0) == 0)
                return null;

            // GetDIBits yields straight (non-premultiplied) alpha; WriteableBitmap wants premultiplied
            // BGRA, so fold alpha into the color channels — otherwise soft icon edges halo.
            for (int i = 0; i < data.Length; i += 4)
            {
                byte a = data[i + 3];
                if (a == 255) continue;
                data[i] = (byte)(data[i] * a / 255);
                data[i + 1] = (byte)(data[i + 1] * a / 255);
                data[i + 2] = (byte)(data[i + 2] * a / 255);
            }

            return (data, w, h);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hdc != IntPtr.Zero)
                DeleteDC(hdc);
            if (hbitmap != IntPtr.Zero)
                DeleteObject(hbitmap);
            if (shellItem is not null)
                Marshal.ReleaseComObject(shellItem);
        }
    }

    // ── Interop ──────────────────────────────────────────────────────────────

    private static Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    // The image isn't ready yet — retry the extract (IShellItemImageFactory contract).
    private const int E_PENDING = unchecked((int)0x8000000A);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr handle, int c, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc, IntPtr hbm, uint start, uint cLines,
        [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint usage);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);
}
