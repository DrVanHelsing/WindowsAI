using System.Data.OleDb;
using WindowsAI.Core.Context;

namespace WindowsAI.Services.Search;

/// <summary>
/// Local-first retrieval backed by the Windows Search index (SYSTEMINDEX) queried over
/// OLE DB. No crawling, no extra index: whatever Windows has already indexed under the
/// user's profile is searchable instantly. First slice of the hybrid RAG pipeline —
/// keyword recall now; embedding re-rank can be layered on top later.
/// </summary>
public sealed class WindowsSearchRetriever : ILocalContextRetriever
{
    private const string ConnectionString =
        "Provider=Search.CollatorDSO;Extended Properties='Application=Windows'";

    /// <summary>Max characters of file content used per snippet.</summary>
    private const int SnippetLength = 700;

    /// <summary>Extensions we are willing to read raw for snippets (plain text formats).</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".json", ".xml", ".yaml", ".yml", ".ini", ".csv",
        ".cs", ".py", ".js", ".ts", ".html", ".css", ".ps1", ".bat", ".cmd", ".sql", ".toml",
    };

    public async Task<IReadOnlyList<LocalDocument>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken = default)
    {
        try
        {
            // OleDb is synchronous under the hood — keep it off the UI thread.
            return await Task.Run(() => SearchCore(query, maxResults, cancellationToken), cancellationToken);
        }
        catch
        {
            return [];
        }
    }

    private static List<LocalDocument> SearchCore(string query, int maxResults, CancellationToken cancellationToken)
    {
        var terms = query.Replace("'", "''").Trim();
        if (terms.Length == 0)
            return [];

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');

        // FREETEXT matches natural language against indexed filename + content.
        // AutoSummary gives an index-provided excerpt when available.
        var sql = $"""
            SELECT TOP {maxResults * 3}
                   System.ItemPathDisplay, System.ItemName, System.Search.AutoSummary
            FROM   SYSTEMINDEX
            WHERE  SCOPE='file:{profile}'
              AND  FREETEXT(*, '{terms}')
            ORDER BY System.Search.Rank DESC
            """;

        var results = new List<LocalDocument>();

        using var connection = new OleDbConnection(ConnectionString);
        connection.Open();
        using var command = new OleDbCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (results.Count < maxResults && reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.IsDBNull(0))
                continue;

            var path = reader.GetString(0);
            var name = reader.IsDBNull(1) ? System.IO.Path.GetFileName(path) : reader.GetString(1);
            var summary = reader.IsDBNull(2) ? null : reader.GetString(2);

            var snippet = BuildSnippet(path, summary);
            if (snippet is null)
                continue; // nothing readable to give the model — skip

            results.Add(new LocalDocument(path, name, snippet));
        }

        return results;
    }

    /// <summary>Index summary if present; otherwise the head of the file for plain-text types.</summary>
    private static string? BuildSnippet(string path, string? summary)
    {
        if (!string.IsNullOrWhiteSpace(summary))
            return Truncate(summary.ReplaceLineEndings(" ").Trim());

        try
        {
            var extension = System.IO.Path.GetExtension(path);
            if (!TextExtensions.Contains(extension))
                return null;

            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 2_000_000)
                return null;

            using var stream = new StreamReader(path);
            var buffer = new char[SnippetLength];
            var read = stream.Read(buffer, 0, buffer.Length);
            return read <= 0 ? null : new string(buffer, 0, read).ReplaceLineEndings(" ").Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string text)
        => text.Length <= SnippetLength ? text : text[..SnippetLength].TrimEnd() + "…";
}
