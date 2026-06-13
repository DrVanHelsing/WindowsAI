using WindowsAI.Services.Interop;

namespace WindowsAI.Services.Hotkeys;

/// <summary>A global hotkey: modifier flags plus a virtual-key code.</summary>
public readonly record struct HotkeyDefinition(uint Modifiers, uint VirtualKey)
{
    /// <summary>Default summon hotkey: Alt+Space (the same chord PowerToys Run popularized).</summary>
    public static HotkeyDefinition Default { get; } = new(NativeMethods.MOD_ALT, 0x20 /* VK_SPACE */);

    /// <summary>
    /// Default writing-tools hotkey: Ctrl+Shift+Space. Pressed over a text selection in any app,
    /// it opens the writing-tools menu. Chosen to avoid the OS-reserved and app menu-mnemonic
    /// chords (plain Alt+letter) while staying close to the Alt+Space summon.
    /// </summary>
    public static HotkeyDefinition WritingTools { get; } =
        new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, 0x20 /* VK_SPACE */);
}
