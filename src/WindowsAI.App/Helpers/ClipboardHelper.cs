using Windows.ApplicationModel.DataTransfer;

namespace WindowsAI_App.Helpers;

/// <summary>
/// Thin wrappers over the WinRT clipboard for the writing-tools flow, which has to borrow the
/// clipboard to read the selection (Ctrl+C) and inject the result (Ctrl+V), then put back
/// whatever the user had there. Text-only — that's all writing tools moves.
/// </summary>
public static class ClipboardHelper
{
    /// <summary>Returns the clipboard's text, or null if it holds none (or reading failed).</summary>
    public static async Task<string?> TryGetTextAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
                return null;
            return await content.GetTextAsync();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Places text on the clipboard (best-effort; clipboard contention is swallowed).</summary>
    public static void SetText(string text)
    {
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush(); // survive our own process if needed
        }
        catch
        {
            // A transient clipboard lock from another app — nothing we can do; skip.
        }
    }

    /// <summary>Clears the clipboard (used when the user had nothing there to restore).</summary>
    public static void Clear()
    {
        try { Clipboard.Clear(); } catch { /* ignore */ }
    }
}
