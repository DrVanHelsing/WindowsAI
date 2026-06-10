using WindowsAI.Services.Interop;

namespace WindowsAI.Services.Hotkeys;

/// <summary>
/// Registers a single global hotkey against a window and raises <see cref="Pressed"/> when
/// it fires. Uses a window subclass to observe <c>WM_HOTKEY</c> without disturbing WinUI's
/// own message handling. Attach to any always-present HWND (e.g. the hidden overlay window).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x4157; // "AW"
    private const nuint SubclassId = 1;

    private readonly IntPtr _hwnd;
    private readonly NativeMethods.SubclassProc _proc; // held to prevent GC of the callback
    private bool _registered;

    /// <summary>Raised on the UI thread when the hotkey is pressed.</summary>
    public event Action? Pressed;

    public HotkeyManager(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = SubclassProc;
        NativeMethods.SetWindowSubclass(_hwnd, _proc, SubclassId, 0);
    }

    /// <summary>(Re)registers the given hotkey. Returns false if the chord is already taken.</summary>
    public bool Register(HotkeyDefinition hotkey)
    {
        if (_registered)
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);

        _registered = NativeMethods.RegisterHotKey(
            _hwnd, HotkeyId, hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, hotkey.VirtualKey);
        return _registered;
    }

    private IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (msg == NativeMethods.WM_HOTKEY && (int)wParam == HotkeyId)
        {
            Pressed?.Invoke();
            return IntPtr.Zero;
        }

        return NativeMethods.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_registered)
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);

        NativeMethods.RemoveWindowSubclass(_hwnd, _proc, SubclassId);
    }
}
