namespace WindowsAI_App.ViewModels;

/// <summary>A file queued in the composer, waiting to be sent with the next message.</summary>
/// <remarks>
/// Plain class with get-only properties (not a record): types reachable from x:Bind
/// item templates must not look settable to the WinUI XamlTypeInfo generator.
/// </remarks>
public sealed class PendingAttachment
{
    /// <summary>Image types we send to vision-capable models as multimodal content.</summary>
    private static readonly Dictionary<string, string> ImageMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
    };

    public PendingAttachment(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        MediaType = ImageMediaTypes.GetValueOrDefault(System.IO.Path.GetExtension(path));
    }

    /// <summary>Full file-system path.</summary>
    public string Path { get; }

    /// <summary>File name shown on the chip.</summary>
    public string Name { get; }

    /// <summary>Image MIME type, or null when this is a document attachment.</summary>
    public string? MediaType { get; }

    public bool IsImage => MediaType is not null;

    /// <summary>True if the path points at a type we can attach (image or extractable document).</summary>
    public static bool IsImagePath(string path) =>
        ImageMediaTypes.ContainsKey(System.IO.Path.GetExtension(path));

    /// <summary>Extensions offered in the file picker (images + extractable documents).</summary>
    public static IEnumerable<string> ImageExtensions => ImageMediaTypes.Keys;
}
