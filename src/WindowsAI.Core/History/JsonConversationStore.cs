using System.Text.Json;

namespace WindowsAI.Core.History;

/// <summary>
/// File-backed conversation store. Each conversation is one JSON file under
/// <c>%LOCALAPPDATA%\WindowsAI\conversations\{id}.json</c> so saving one session never
/// rewrites the others. Corrupt files are skipped rather than failing the whole list.
/// </summary>
public sealed class JsonConversationStore : IConversationStore
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _dir;

    public JsonConversationStore(string? directory = null)
    {
        _dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAI",
            "conversations");
    }

    public async Task<IReadOnlyList<StoredConversation>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<StoredConversation>();
        if (!Directory.Exists(_dir))
            return result;

        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                await using var fs = File.OpenRead(file);
                var convo = await JsonSerializer.DeserializeAsync<StoredConversation>(fs, s_json, ct);
                if (convo is not null)
                    result.Add(convo);
            }
            catch
            {
                // Skip unreadable/corrupt files — a bad record shouldn't hide the rest.
            }
        }

        return result
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.UpdatedAt)
            .ToList();
    }

    public async Task<StoredConversation?> GetAsync(string id, CancellationToken ct = default)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<StoredConversation>(fs, s_json, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(StoredConversation conversation, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_dir);
        await using var fs = File.Create(PathFor(conversation.Id));
        await JsonSerializer.SerializeAsync(fs, conversation, s_json, ct);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var path = PathFor(id);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string PathFor(string id) => Path.Combine(_dir, $"{id}.json");
}
