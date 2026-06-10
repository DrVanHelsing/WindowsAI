using WindowsAI.Services.Interop;

namespace WindowsAI.Services.Tray;

/// <summary>
/// A pure Win32 system-tray icon: <c>Shell_NotifyIcon</c> for the icon and
/// <c>TrackPopupMenuEx</c> for its right-click menu, hosted on a hidden message window.
/// </summary>
/// <remarks>
/// This deliberately avoids H.NotifyIcon's XAML <c>ContextFlyout</c>, which needs a live
/// <c>XamlRoot</c> from an <em>activated</em> window — something the always-hidden overlay
/// never has, so its menu silently never fired. A native menu has no such dependency, renders
/// at the cursor, and matches the system look. Construct on the UI thread (it needs that
/// thread's message pump) and dispose to remove the icon and tear down the window.
/// </remarks>
public sealed class NativeTrayIcon : IDisposable
{
    private const int TrayCallbackMessage = NativeMethods.WM_APP + 0x1; // our uCallbackMessage
    private const int TrayIconId = 0x4157; // "AW"

    private sealed record MenuEntry(uint Id, string? Text, Action? Action, bool IsSeparator);

    private readonly NativeMethods.WndProc _wndProc; // held to prevent GC of the callback
    private readonly uint _taskbarCreatedMessage;
    private readonly List<MenuEntry> _entries = new();
    private readonly string _tooltip;
    private readonly IntPtr _icon;

    private IntPtr _hwnd;
    private uint _nextId = 1;
    private bool _added;
    private bool _disposed;

    /// <summary>Raised on the UI thread when the icon is left-clicked.</summary>
    public event Action? LeftClicked;

    /// <param name="tooltip">Hover tooltip text.</param>
    /// <param name="iconPath">Absolute path to a <c>.ico</c> file.</param>
    public NativeTrayIcon(string tooltip, string iconPath)
    {
        _tooltip = tooltip;
        _wndProc = WndProc;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _icon = NativeMethods.LoadImage(
            IntPtr.Zero, iconPath, NativeMethods.IMAGE_ICON, 0, 0,
            NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE);

        CreateMessageWindow();
    }

    /// <summary>Adds a clickable menu item. Items appear in the order added.</summary>
    public void AddMenuItem(string text, Action onClick)
        => _entries.Add(new MenuEntry(_nextId++, text, onClick, IsSeparator: false));

    /// <summary>Adds a horizontal separator to the menu.</summary>
    public void AddSeparator()
        => _entries.Add(new MenuEntry(0, null, null, IsSeparator: true));

    /// <summary>Adds the icon to the notification area. Call once after configuring the menu.</summary>
    public void Show()
    {
        if (_added || _hwnd == IntPtr.Zero)
            return;

        var data = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON
                   | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon,
            szTip = _tooltip,
        };

        if (NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
            _added = true;
        }
    }

    private void CreateMessageWindow()
    {
        var hInstance = NativeMethods.GetModuleHandle(null);
        var className = "WindowsAITray_" + Guid.NewGuid().ToString("N");

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            lpszClassName = className,
        };

        NativeMethods.RegisterClassEx(ref wc);

        // A normal (never-shown) top-level window — not HWND_MESSAGE — so TrackPopupMenuEx
        // and SetForegroundWindow behave correctly for the menu.
        _hwnd = NativeMethods.CreateWindowEx(
            0, className, "WindowsAI Tray", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == TrayCallbackMessage)
        {
            // With NOTIFYICON_VERSION_4 the triggering mouse message is the low word of lParam.
            int mouseMsg = (int)((long)lParam & 0xFFFF);
            switch (mouseMsg)
            {
                case NativeMethods.WM_LBUTTONUP:
                    LeftClicked?.Invoke();
                    return IntPtr.Zero;

                case NativeMethods.WM_RBUTTONUP:
                case NativeMethods.WM_CONTEXTMENU:
                    ShowContextMenu();
                    return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        // Re-add the icon if Explorer (the shell) restarts.
        if (msg == _taskbarCreatedMessage)
        {
            _added = false;
            Show();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            foreach (var e in _entries)
            {
                if (e.IsSeparator)
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
                else
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, e.Id, e.Text);
            }

            // Required so the menu dismisses correctly when the user clicks elsewhere.
            NativeMethods.SetForegroundWindow(_hwnd);
            NativeMethods.GetCursorPos(out var pt);

            uint cmd = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                pt.X, pt.Y, _hwnd, IntPtr.Zero);

            NativeMethods.PostMessage(_hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (cmd != 0)
                _entries.FirstOrDefault(e => e.Id == cmd)?.Action?.Invoke();
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_added)
        {
            var data = new NativeMethods.NOTIFYICONDATA
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = TrayIconId,
            };
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        if (_icon != IntPtr.Zero)
            NativeMethods.DestroyIcon(_icon);

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
