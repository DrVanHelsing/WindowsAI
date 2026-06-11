using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace WindowsAI.Core.Context;

/// <summary>
/// Pulls plain text out of common document formats so an attached file can be fed to the
/// model as context. Plain text/markdown is read as-is; PDF goes through PdfPig; .docx is
/// unzipped and its <c>word/document.xml</c> stripped of tags (no Office dependency).
/// </summary>
public static partial class FileTextExtractor
{
    /// <summary>Extensions this extractor knows how to turn into text.</summary>
    public static readonly IReadOnlyCollection<string> SupportedExtensions = new[]
    {
        ".txt", ".md", ".markdown", ".log", ".csv", ".json", ".xml", ".yml", ".yaml",
        ".pdf", ".docx",
    };

    /// <summary>True if <paramref name="path"/> has an extension we can extract text from.</summary>
    public static bool CanExtract(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Extracts text from the file, dispatching on its extension.</summary>
    public static async Task<string> ExtractAsync(string path, CancellationToken ct = default)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => ExtractPdf(path),
            ".docx" => ExtractDocx(path),
            _ => await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
        };
    }

    private static string ExtractPdf(string path)
    {
        using var doc = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    private static string ExtractDocx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null)
            return string.Empty;

        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();

        // Map paragraph / line-break boundaries to newlines, then drop every remaining tag.
        xml = ParagraphEnd().Replace(xml, "\n");
        xml = LineBreak().Replace(xml, "\n");
        var text = AnyTag().Replace(xml, string.Empty);
        return WebUtility.HtmlDecode(text);
    }

    [GeneratedRegex(@"</w:p>")]
    private static partial Regex ParagraphEnd();

    [GeneratedRegex(@"<w:br\b[^>]*/?>")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();
}
