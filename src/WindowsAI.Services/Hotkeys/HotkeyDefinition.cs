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

    /// <summary>
    /// Default visual-intelligence hotkey: Alt+Shift+S. Dims the screen, lets the user drag a
    /// region, and asks a vision model about it. "S" for screen/snip; chosen to avoid Win+Shift+S
    /// (the OS Snipping Tool chord) while staying mnemonic.
    /// </summary>
    public static HotkeyDefinition VisualIntelligence { get; } =
        new(NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, 0x53 /* VK_S */);
}
