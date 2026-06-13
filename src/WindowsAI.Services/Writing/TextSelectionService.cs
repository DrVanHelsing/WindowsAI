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
    /// <summary>Synthesizes Ctrl+C to copy the focused control's selection to the clipboard.</summary>
    public void SendCopy() => SendCtrlChord(NativeMethods.VK_C);

    /// <summary>Synthesizes Ctrl+V to paste the clipboard over the focused control's selection.</summary>
    public void SendPaste() => SendCtrlChord(NativeMethods.VK_V);

    private static void SendCtrlChord(ushort key)
    {
        var inputs = new[]
        {
            KeyDown(NativeMethods.VK_CONTROL),
            KeyDown(key),
            KeyUp(key),
            KeyUp(NativeMethods.VK_CONTROL),
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
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
