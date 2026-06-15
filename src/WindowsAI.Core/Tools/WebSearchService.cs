using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Tools;

/// <summary>A single web-search hit surfaced to the model and (as a link chip) the user.</summary>
public sealed record WebResult(string Title, string Url, string Snippet);

/// <summary>
/// Backs the <c>search_web</c> agent tool via Google's Programmable Search (Custom Search JSON API).
/// The Search Engine id (<c>cx</c>) lives in settings; the API key is read from the secret store.
/// Reuses one static <see cref="HttpClient"/> (mirrors <c>ModelCatalog</c>).
/// </summary>
public sealed class WebSearchService
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;

    public WebSearchService(ISettingsStore settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    /// <summary>True when the engine id and API key are both present and the feature is enabled.</summary>
    public bool IsConfigured
    {
        get
        {
            var ws = _settings.Current.WebSearch;
            return ws.Enabled
                && !string.IsNullOrWhiteSpace(ws.SearchEngineId)
                && !string.IsNullOrWhiteSpace(_secrets.Get(SecretKeys.GoogleSearchApiKey));
        }
    }

    /// <summary>
    /// Runs a search and returns up to <paramref name="count"/> results. Throws
    /// <see cref="InvalidOperationException"/> if not configured; surfaces HTTP errors to the caller.
    /// </summary>
    public async Task<IReadOnlyList<WebResult>> SearchAsync(string query, int count = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<WebResult>();

        var ws = _settings.Current.WebSearch;
        var key = _secrets.Get(SecretKeys.GoogleSearchApiKey);
        if (string.IsNullOrWhiteSpace(ws.SearchEngineId) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Web search isn't configured. Add a Search Engine id and API key in Settings.");

        var num = Math.Clamp(count, 1, 10);
        var url = "https://www.googleapis.com/customsearch/v1" +
                  $"?key={Uri.EscapeDataString(key)}" +
                  $"&cx={Uri.EscapeDataString(ws.SearchEngineId)}" +
                  $"&q={Uri.EscapeDataString(query)}" +
                  $"&num={num}";

        using var response = await s_http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return Parse(json);
    }

    /// <summary>Parses the Custom Search JSON payload into a flat result list. Public for unit tests.</summary>
    public static IReadOnlyList<WebResult> Parse(string json)
    {
        var payload = JsonSerializer.Deserialize<CustomSearchResponse>(json);
        if (payload?.Items is null)
            return Array.Empty<WebResult>();

        return payload.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Link))
            .Select(i => new WebResult(
                Title: i.Title?.Trim() ?? i.Link!,
                Url: i.Link!,
                Snippet: (i.Snippet ?? string.Empty).Replace("\n", " ").Trim()))
            .ToList();
    }

    private sealed class CustomSearchResponse
    {
        [JsonPropertyName("items")]
        public List<Item>? Items { get; set; }
    }

    private sealed class Item
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("snippet")]
        public string? Snippet { get; set; }
    }
}
