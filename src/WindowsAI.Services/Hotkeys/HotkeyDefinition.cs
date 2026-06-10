using WindowsAI.Services.Interop;

namespace WindowsAI.Services.Hotkeys;

/// <summary>A global hotkey: modifier flags plus a virtual-key code.</summary>
public readonly record struct HotkeyDefinition(uint Modifiers, uint VirtualKey)
{
    /// <summary>Default summon hotkey: Alt+Space (the same chord PowerToys Run popularized).</summary>
    public static HotkeyDefinition Default { get; } = new(NativeMethods.MOD_ALT, 0x20 /* VK_SPACE */);
}
