using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsAI.Core.Configuration;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Builds the model picker list: locally installed Ollama models (queried live from
/// <c>/api/tags</c>) plus a curated set of cloud models. Cloud entries always appear so
/// users can select one and then add the key in Settings.
/// </summary>
public sealed class ModelCatalog : IModelCatalog
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly ISettingsStore _settings;

    public ModelCatalog(ISettingsStore settings) => _settings = settings;

    public async Task<IReadOnlyList<ModelOption>> GetModelsAsync(CancellationToken ct = default)
    {
        var models = new List<ModelOption>();
        models.AddRange(await GetLocalModelsAsync(ct));
        models.AddRange(GetCloudModels());
        return models;
    }

    private async Task<IReadOnlyList<ModelOption>> GetLocalModelsAsync(CancellationToken ct)
    {
        var result = new List<ModelOption>();
        try
        {
            var url = _settings.Current.LocalModel.Endpoint.TrimEnd('/') + "/api/tags";
            var tags = await s_http.GetFromJsonSafeAsync<OllamaTagsResponse>(url, ct);
            if (tags?.Models is not null)
            {
                foreach (var m in tags.Models.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
                    result.Add(new ModelOption("ollama", m.Name, $"{m.Name} (local)", isLocal: true));
            }
        }
        catch
        {
            // Ollama not running / unreachable — just show no local models.
        }

        return result;
    }

    private IReadOnlyList<ModelOption> GetCloudModels()
    {
        var s = _settings.Current;
        return
        [
            new ModelOption("openrouter", s.OpenRouter.DefaultModelId, $"{s.OpenRouter.DefaultModelId} (OpenRouter)", isLocal: false),
            new ModelOption("openrouter", "deepseek/deepseek-r1:free", "deepseek-r1:free (OpenRouter)", isLocal: false),
            new ModelOption("gemini", s.Gemini.DefaultModelId, $"{s.Gemini.DefaultModelId} (Gemini)", isLocal: false),
            new ModelOption("gemini", "gemini-2.5-pro", "gemini-2.5-pro (Gemini)", isLocal: false),
        ];
    }

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaTag>? Models { get; set; }
    }

    private sealed class OllamaTag
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}

file static class HttpClientJsonExtensions
{
    public static async Task<T?> GetFromJsonSafeAsync<T>(this HttpClient http, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct);
    }
}
