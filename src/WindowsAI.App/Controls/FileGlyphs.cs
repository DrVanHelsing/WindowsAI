namespace WindowsAI_App.Controls;

/// <summary>
/// Maps a file name to a Segoe Fluent glyph by extension, so RAG source chips show a
/// recognizable file-type icon instead of a generic page. Bound from XAML as a static
/// function: <c>Glyph="{x:Bind controls:FileGlyphs.For(Name)}"</c>.
/// </summary>
public static class FileGlyphs
{
    // All codepoints are standard Segoe Fluent / MDL2 Assets glyphs.
    private const string Page = "";      // generic document (fallback)
    private const string Document = "";  // text / office doc
    private const string Pdf = "";       // PDF
    private const string Photo = "";     // image (Photo2)
    private const string Video = "";     // video
    private const string Audio = "";     // audio (MusicInfo)
    private const string Archive = "";   // zip / archive (ZipFolder)
    private const string Code = "";      // source code

    public static string For(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return Page;

        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1)
            return Page;

        return name[(dot + 1)..].ToLowerInvariant() switch
        {
            "txt" or "md" or "markdown" or "log" or "rtf" or "doc" or "docx" or "odt"
                => Document,
            "pdf"
                => Pdf,
            "png" or "jpg" or "jpeg" or "gif" or "bmp" or "webp" or "svg" or "tif" or "tiff" or "ico" or "heic"
                => Photo,
            "mp4" or "mov" or "avi" or "mkv" or "wmv" or "webm" or "m4v"
                => Video,
            "mp3" or "wav" or "flac" or "m4a" or "aac" or "ogg" or "wma"
                => Audio,
            "zip" or "rar" or "7z" or "tar" or "gz" or "bz2"
                => Archive,
            "cs" or "js" or "ts" or "tsx" or "jsx" or "py" or "java" or "cpp" or "cc" or "c" or "h" or "hpp"
                or "go" or "rs" or "rb" or "php" or "html" or "htm" or "css" or "json" or "xml"
                or "yml" or "yaml" or "ps1" or "sh" or "sql" or "toml" or "ini"
                => Code,
            _ => Page,
        };
    }
}
