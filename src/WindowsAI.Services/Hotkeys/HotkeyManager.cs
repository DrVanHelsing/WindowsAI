using WindowsAI.Services.Interop;

namespace WindowsAI.Services.Hotkeys;

/// <summary>
/// Registers one or more global hotkeys against a window and invokes each chord's callback when
/// it fires. A single window subclass observes <c>WM_HOTKEY</c> for all of them (without
/// disturbing WinUI's own message handling) and dispatches by hotkey id. Attach to any
/// always-present HWND (e.g. the hidden overlay window).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int FirstHotkeyId = 0x4157; // "AW"
    private const nuint SubclassId = 1;

    private readonly IntPtr _hwnd;
    private readonly NativeMethods.SubclassProc _proc; // held to prevent GC of the callback
    private readonly Dictionary<int, Registration> _hotkeys = new();
    private int _nextId = FirstHotkeyId;

    private sealed record Registration(HotkeyDefinition Definition, Action OnPressed)
    {
        public bool Registered { get; set; }
    }

    public HotkeyManager(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = SubclassProc;
        NativeMethods.SetWindowSubclass(_hwnd, _proc, SubclassId, 0);
    }

    /// <summary>
    /// Reserves a hotkey slot with its callback and attempts to register the chord. Returns an
    /// id usable with <see cref="TryRegister"/> for later retries. The chord may not be live yet
    /// (another app can still hold it); check the return of <see cref="TryRegister"/>.
    /// </summary>
    public int Add(HotkeyDefinition hotkey, Action onPressed)
    {
        var id = _nextId++;
        _hotkeys[id] = new Registration(hotkey, onPressed);
        TryRegister(id);
        return id;
    }

    /// <summary>(Re)attempts to register a previously-added hotkey. Returns false if the chord is taken.</summary>
    public bool TryRegister(int id)
    {
        if (!_hotkeys.TryGetValue(id, out var reg))
            return false;

        if (reg.Registered)
            NativeMethods.UnregisterHotKey(_hwnd, id);

        reg.Registered = NativeMethods.RegisterHotKey(
            _hwnd, id, reg.Definition.Modifiers | NativeMethods.MOD_NOREPEAT, reg.Definition.VirtualKey);
        return reg.Registered;
    }

    /// <summary>True once every added hotkey has successfully registered its chord.</summary>
    public bool AllRegistered => _hotkeys.Values.All(r => r.Registered);

    /// <summary>Re-attempts registration for any hotkey not yet live. Returns true when all are live.</summary>
    public bool TryRegisterPending()
    {
        foreach (var (id, reg) in _hotkeys)
            if (!reg.Registered)
                TryRegister(id);
        return AllRegistered;
    }

    private IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (msg == NativeMethods.WM_HOTKEY && _hotkeys.TryGetValue((int)wParam, out var reg))
        {
            reg.OnPressed();
            return IntPtr.Zero;
        }

        return NativeMethods.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        foreach (var (id, reg) in _hotkeys)
            if (reg.Registered)
                NativeMethods.UnregisterHotKey(_hwnd, id);

        NativeMethods.RemoveWindowSubclass(_hwnd, _proc, SubclassId);
    }
}
