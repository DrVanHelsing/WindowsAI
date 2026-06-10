using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Context;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Embeddings from the local Ollama server (POST /api/embed) using the configured
/// embedding model (default <c>nomic-embed-text</c>). Failure-silent: any error — server
/// down, model not pulled, malformed response — yields <c>null</c> so local retrieval
/// falls back to the keyword ranking.
/// </summary>
public sealed class OllamaEmbeddingClient : IEmbeddingClient
{
    private readonly ISettingsStore _settings;
    private readonly HttpClient _http;

    public OllamaEmbeddingClient(ISettingsStore settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<IReadOnlyList<float[]>?> EmbedAsync(
        IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
            return [];

        try
        {
            var local = _settings.Current.LocalModel;
            var endpoint = local.Endpoint.TrimEnd('/');
            var request = new EmbedRequest(local.EmbeddingModelId, inputs);

            using var response = await _http.PostAsJsonAsync(
                $"{endpoint}/api/embed", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken);
            if (payload?.Embeddings is null || payload.Embeddings.Count != inputs.Count)
                return null;

            return payload.Embeddings;
        }
        catch
        {
            return null; // failure-silent — retrieval continues with keyword order
        }
    }

    private sealed record EmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbedResponse(
        [property: JsonPropertyName("embeddings")] IReadOnlyList<float[]>? Embeddings);
}
