using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Tools;

/// <summary>A single web-search hit surfaced to the model and (as a link chip) the user.</summary>
public sealed record WebResult(string Title, string Url, string Snippet);

/// <summary>
/// Backs the <c>search_web</c> agent tool via Tavily's Search API (https://api.tavily.com/search).
/// The API key is read from the secret store; no engine id or other config is required. Tavily's
/// free tier suits an occasional agent tool, and its results come pre-trimmed for LLM use.
/// Reuses one static <see cref="HttpClient"/> (mirrors <c>ModelCatalog</c>).
/// </summary>
public sealed class WebSearchService
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;

    public WebSearchService(ISettingsStore settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    /// <summary>True when the feature is enabled and a Tavily API key is present.</summary>
    public bool IsConfigured
        => _settings.Current.WebSearch.Enabled
           && !string.IsNullOrWhiteSpace(_secrets.Get(SecretKeys.TavilyApiKey));

    /// <summary>
    /// Runs a search and returns up to <paramref name="count"/> results. Throws
    /// <see cref="InvalidOperationException"/> if not configured; surfaces HTTP errors to the caller.
    /// </summary>
    public async Task<IReadOnlyList<WebResult>> SearchAsync(string query, int count = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<WebResult>();

        var key = _secrets.Get(SecretKeys.TavilyApiKey);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Web search isn't configured. Add a Tavily API key in Settings.");

        var max = Math.Clamp(count, 1, 10);
        var payload = JsonSerializer.Serialize(new TavilyRequest { Query = query, MaxResults = max });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

        using var response = await s_http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return Parse(json);
    }

    /// <summary>Parses the Tavily Search JSON payload into a flat result list. Public for unit tests.</summary>
    public static IReadOnlyList<WebResult> Parse(string json)
    {
        var payload = JsonSerializer.Deserialize<TavilyResponse>(json);
        if (payload?.Results is null)
            return Array.Empty<WebResult>();

        return payload.Results
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Select(r => new WebResult(
                Title: r.Title?.Trim() is { Length: > 0 } t ? t : r.Url!,
                Url: r.Url!,
                Snippet: (r.Content ?? string.Empty).Replace("\n", " ").Trim()))
            .ToList();
    }

    private sealed class TavilyRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("max_results")]
        public int MaxResults { get; set; }
    }

    private sealed class TavilyResponse
    {
        [JsonPropertyName("results")]
        public List<Result>? Results { get; set; }
    }

    private sealed class Result
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
