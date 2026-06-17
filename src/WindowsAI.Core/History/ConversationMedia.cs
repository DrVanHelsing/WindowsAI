namespace WindowsAI.Core.History;

/// <summary>
/// Stores images that belong to a saved conversation (e.g. a Visual Intelligence screen-region
/// crop) in a stable folder next to the conversation JSON, so the saved transcript still renders
/// its thumbnail long after the original temp capture has been cleaned up. Conversations reference
/// the saved file with a normal markdown image (<c>![region](path)</c>), so both the history list
/// and the chat surface render it with no extra plumbing.
/// </summary>
public static class ConversationMedia
{
    /// <summary>Default media folder: <c>%LOCALAPPDATA%\WindowsAI\conversations\media</c>.</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowsAI",
        "conversations",
        "media");

    /// <summary>
    /// Copies <paramref name="imageBytes"/> into the media folder under a fresh, stable file name and
    /// returns its full path (the value to embed in a markdown image). Returns null on any I/O failure —
    /// a missing image must never break saving the conversation text.
    /// </summary>
    public static string? Save(byte[] imageBytes, string extension = "png", string? directory = null)
    {
        try
        {
            var dir = directory ?? DefaultDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{Guid.NewGuid():n}.{extension.TrimStart('.')}");
            File.WriteAllBytes(path, imageBytes);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
