using System.Runtime.InteropServices;
using WindowsAI.Services.Interop;

namespace WindowsAI.Services.Writing;

/// <summary>
/// Synthesizes the copy/paste chords used to read and replace the selection in whatever app
/// currently has focus. The clipboard route is the plan's reliable injection path: it works
/// uniformly across Win32, WinUI, browser, and Electron apps where UI Automation's
/// <c>TextPattern</c> is patchy. The caller owns the clipboard contents (save → set → restore)
/// because clipboard access is simplest from the UI thread; this service only presses keys.
/// </summary>
public sealed class TextSelectionService
{
    /// <summary>Synthesizes Ctrl+C to copy the focused control's selection. Returns false if the OS rejected the input.</summary>
    public bool SendCopy() => SendCtrlChord(NativeMethods.VK_C);

    /// <summary>Synthesizes Ctrl+V to paste the clipboard over the focused control's selection. Returns false if the OS rejected the input.</summary>
    public bool SendPaste() => SendCtrlChord(NativeMethods.VK_V);

    /// <summary>
    /// Injects a Ctrl+&lt;key&gt; chord and reports whether the OS accepted every synthesized event.
    /// <c>SendInput</c> returns the number of events inserted; a short count means input was blocked
    /// (e.g. by a higher-integrity foreground window) so the caller can avoid a bad clipboard swap.
    /// </summary>
    /// <remarks>
    /// We're usually invoked straight off a global hotkey whose own modifiers (e.g. Ctrl+Shift+Space)
    /// are still physically held when we fire. A bare Ctrl+C would then reach the target as
    /// Ctrl+Shift+C — not "copy" — so the read-back comes up empty and the menu never opens. We first
    /// synthesize key-ups for every modifier (both L/R variants) to neutralize whatever the user is
    /// holding, then press a clean Ctrl+&lt;key&gt;.
    /// </remarks>
    private static bool SendCtrlChord(ushort key)
    {
        var inputs = new[]
        {
            // Release any physically-held modifiers so the chord isn't polluted by them.
            KeyUp(NativeMethods.VK_LSHIFT),
            KeyUp(NativeMethods.VK_RSHIFT),
            KeyUp(NativeMethods.VK_SHIFT),
            KeyUp(NativeMethods.VK_LMENU),
            KeyUp(NativeMethods.VK_RMENU),
            KeyUp(NativeMethods.VK_MENU),
            KeyUp(NativeMethods.VK_LWIN),
            KeyUp(NativeMethods.VK_RWIN),
            KeyUp(NativeMethods.VK_LCONTROL),
            KeyUp(NativeMethods.VK_RCONTROL),
            KeyUp(NativeMethods.VK_CONTROL),

            // Clean Ctrl+<key>.
            KeyDown(NativeMethods.VK_CONTROL),
            KeyDown(key),
            KeyUp(key),
            KeyUp(NativeMethods.VK_CONTROL),
        };
        var inserted = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        return inserted == (uint)inputs.Length;
    }

    private static NativeMethods.INPUT KeyDown(ushort vk) => Key(vk, 0);
    private static NativeMethods.INPUT KeyUp(ushort vk) => Key(vk, NativeMethods.KEYEVENTF_KEYUP);

    private static NativeMethods.INPUT Key(ushort vk, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = flags },
        },
    };
}
