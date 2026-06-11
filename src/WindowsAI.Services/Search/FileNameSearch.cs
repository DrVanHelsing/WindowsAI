using System.Data.OleDb;
using WindowsAI.Core.Search;

namespace WindowsAI.Services.Search;

/// <summary>
/// Fast file/folder *name* lookup against the Windows Search index for the overlay's
/// launcher list. Unlike <see cref="WindowsSearchRetriever"/> (content FREETEXT for RAG),
/// this matches names only — prefix hits first, then contains — which is what a
/// Windows-Search-style launcher shows while you type.
/// </summary>
public sealed class FileNameSearch
{
    private const string ConnectionString =
        "Provider=Search.CollatorDSO;Extended Properties='Application=Windows'";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(() => SearchCore(query, maxResults, ct), ct);
        }
        catch
        {
            return [];
        }
    }

    private static List<SearchHit> SearchCore(string query, int maxResults, CancellationToken ct)
    {
        // LIKE pattern: escape quotes, strip its wildcard metacharacters from user text.
        var sanitized = new string(query.Trim().Where(c => c is not ('%' or '_' or '[' or ']')).ToArray())
            .Replace("'", "''");
        if (sanitized.Length < 2)
            return [];

        var results = new List<SearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var connection = new OleDbConnection(ConnectionString);
        connection.Open();

        // Two passes: names starting with the query, then names merely containing it.
        foreach (var pattern in new[] { $"{sanitized}%", $"%{sanitized}%" })
        {
            if (results.Count >= maxResults)
                break;
            QueryPattern(connection, pattern, maxResults, results, seen, ct);
        }

        return results;
    }

    private static void QueryPattern(
        OleDbConnection connection, string pattern, int maxResults,
        List<SearchHit> results, HashSet<string> seen, CancellationToken ct)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');
        var sql = $"""
            SELECT TOP {maxResults * 2}
                   System.ItemPathDisplay, System.ItemNameDisplay, System.ItemType
            FROM   SYSTEMINDEX
            WHERE  SCOPE='file:{profile}'
              AND  System.ItemNameDisplay LIKE '{pattern}'
            ORDER BY System.Search.Rank DESC
            """;

        using var command = new OleDbCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (results.Count < maxResults && reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.IsDBNull(0))
                continue;

            var path = reader.GetString(0);
            if (!seen.Add(path))
                continue;

            var name = reader.IsDBNull(1) ? Path.GetFileName(path) : reader.GetString(1);
            var itemType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var kind = string.Equals(itemType, "Directory", StringComparison.OrdinalIgnoreCase)
                ? SearchHitKind.Folder
                : SearchHitKind.File;

            results.Add(new SearchHit(kind, name, Path.GetDirectoryName(path) ?? string.Empty, path));
        }
    }
}
