using System.Text.Json;

namespace WindowsAI.Core.Configuration;

/// <summary>
/// File-backed settings store. Writes a human-readable JSON file under
/// <c>%LOCALAPPDATA%\WindowsAI\settings.json</c>. Corrupt or missing files fall
/// back to defaults so the app always starts.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    public AppSettings Current { get; private set; } = new();

    public JsonSettingsStore(string? filePath = null)
    {
        _path = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAI",
            "settings.json");
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_path))
                return;

            await using var fs = File.OpenRead(_path);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(fs, s_json, ct);
            if (loaded is not null)
                Current = loaded;
        }
        catch
        {
            // Unreadable settings should never block startup — keep defaults.
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var fs = File.Create(_path);
        await JsonSerializer.SerializeAsync(fs, Current, s_json, ct);
    }
}
