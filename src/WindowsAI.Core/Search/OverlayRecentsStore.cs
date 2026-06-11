using System.Text.Json;

namespace WindowsAI.Core.Search;

/// <summary>A previously launched search hit, replayable from the overlay's empty state.</summary>
public sealed class RecentItem
{
    public SearchHitKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; set; }
}

/// <summary>
/// Quick-access memory for the overlay launcher: the last few items the user opened and
/// the last few queries they searched. Plain JSON under
/// <c>%LOCALAPPDATA%\WindowsAI\overlay-recents.json</c>; AI conversations are *not*
/// recorded here — those live in conversation history.
/// </summary>
public sealed class OverlayRecentsStore
{
    private const int MaxItems = 4;
    private const int MaxQueries = 3;

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class Payload
    {
        public List<RecentItem> Items { get; set; } = new();
        public List<string> Queries { get; set; } = new();
    }

    private readonly string _path;
    private Payload? _cache;

    public OverlayRecentsStore(string? filePath = null)
    {
        _path = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAI",
            "overlay-recents.json");
    }

    public async Task<IReadOnlyList<RecentItem>> GetItemsAsync(CancellationToken ct = default)
        => (await LoadAsync(ct)).Items;

    public async Task<IReadOnlyList<string>> GetQueriesAsync(CancellationToken ct = default)
        => (await LoadAsync(ct)).Queries;

    /// <summary>Records a launched hit (most-recent-first, de-duped by target).</summary>
    public async Task RecordItemAsync(SearchHit hit, CancellationToken ct = default)
    {
        var payload = await LoadAsync(ct);
        payload.Items.RemoveAll(i => string.Equals(i.Target, hit.Target, StringComparison.OrdinalIgnoreCase));
        payload.Items.Insert(0, new RecentItem
        {
            Kind = hit.Kind,
            Name = hit.Name,
            Detail = hit.Detail,
            Target = hit.Target,
            OpenedAt = DateTimeOffset.Now,
        });
        if (payload.Items.Count > MaxItems)
            payload.Items.RemoveRange(MaxItems, payload.Items.Count - MaxItems);

        await SaveAsync(payload, ct);
    }

    /// <summary>Records the query that led to a launch (most-recent-first, de-duped).</summary>
    public async Task RecordQueryAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0)
            return;

        var payload = await LoadAsync(ct);
        payload.Queries.RemoveAll(q => string.Equals(q, query, StringComparison.OrdinalIgnoreCase));
        payload.Queries.Insert(0, query);
        if (payload.Queries.Count > MaxQueries)
            payload.Queries.RemoveRange(MaxQueries, payload.Queries.Count - MaxQueries);

        await SaveAsync(payload, ct);
    }

    private async Task<Payload> LoadAsync(CancellationToken ct)
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (File.Exists(_path))
            {
                await using var fs = File.OpenRead(_path);
                _cache = await JsonSerializer.DeserializeAsync<Payload>(fs, s_json, ct);
            }
        }
        catch
        {
            // Corrupt recents are decoration — start fresh rather than fail.
        }

        return _cache ??= new Payload();
    }

    private async Task SaveAsync(Payload payload, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var fs = File.Create(_path);
            await JsonSerializer.SerializeAsync(fs, payload, s_json, ct);
        }
        catch
        {
            // Best effort — losing a recents write must never surface in the UI.
        }
    }
}
