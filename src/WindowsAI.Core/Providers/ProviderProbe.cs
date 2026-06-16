using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsAI.Core.Auth;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Providers;

/// <summary>Outcome of a connection test.</summary>
public enum ProbeStatus { Ok, AuthFailed, Unreachable, Error }

/// <summary>Result of testing a connection: a status, a human message, and any models discovered.</summary>
public sealed record ProbeResult(ProbeStatus Status, string Message, IReadOnlyList<ProviderModel> Models)
{
    public bool Ok => Status == ProbeStatus.Ok;
}

/// <summary>
/// Live model discovery and connection testing for a <see cref="ProviderConnection"/>. Talks the
/// standard endpoints — OpenAI <c>GET /models</c>, Ollama <c>GET /api/tags</c> — so any compatible
/// provider's catalog can be fetched, and "Test connection" reports auth/reachability clearly.
/// </summary>
public sealed class ProviderProbe
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly ISecretStore _secrets;
    private readonly GoogleOAuthService _oauth;

    public ProviderProbe(ISecretStore secrets, GoogleOAuthService oauth)
    {
        _secrets = secrets;
        _oauth = oauth;
    }

    /// <summary>Tests reachability/auth and returns any discovered models.</summary>
    public async Task<ProbeResult> TestAsync(ProviderConnection connection, CancellationToken ct = default)
    {
        try
        {
            var models = await ListModelsAsync(connection, ct);
            return new ProbeResult(ProbeStatus.Ok,
                models.Count > 0 ? $"Connected — {models.Count} model(s) found." : "Connected.",
                models);
        }
        catch (UnauthorizedProbeException ex)
        {
            return new ProbeResult(ProbeStatus.AuthFailed, ex.Message, []);
        }
        catch (HttpRequestException ex)
        {
            return new ProbeResult(ProbeStatus.Unreachable, $"Couldn't reach the endpoint: {ex.Message}", []);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ProbeResult(ProbeStatus.Unreachable, "The endpoint timed out.", []);
        }
        catch (Exception ex)
        {
            return new ProbeResult(ProbeStatus.Error, ex.Message, []);
        }
    }

    /// <summary>Fetches the model list for a connection. Throws on auth/transport errors (for <see cref="TestAsync"/>).</summary>
    public async Task<IReadOnlyList<ProviderModel>> ListModelsAsync(ProviderConnection connection, CancellationToken ct = default)
        => connection.Kind switch
        {
            ProviderKind.Ollama => await ListOllamaAsync(connection, ct),
            ProviderKind.GoogleVertex => connection.Models.Count > 0
                ? connection.Models
                : await ListVertexAsync(connection, ct),
            _ => await ListOpenAiAsync(connection, ct),
        };

    private async Task<IReadOnlyList<ProviderModel>> ListOpenAiAsync(ProviderConnection c, CancellationToken ct)
    {
        var url = c.BaseUrl.TrimEnd('/') + "/models";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var key = _secrets.Get(SecretKeys.ApiKeyFor(c.Id));
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await s_http.SendAsync(request, ct);
        ThrowIfAuthFailed(response.StatusCode);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var payload = await JsonSerializer.DeserializeAsync<OpenAiModelList>(stream, cancellationToken: ct);
        return payload?.Data?
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => new ProviderModel(m.Id))
            .ToList() ?? [];
    }

    private static async Task<IReadOnlyList<ProviderModel>> ListOllamaAsync(ProviderConnection c, CancellationToken ct)
    {
        var url = c.BaseUrl.TrimEnd('/') + "/api/tags";
        using var response = await s_http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var payload = await JsonSerializer.DeserializeAsync<OllamaTags>(stream, cancellationToken: ct);
        return payload?.Models?
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .Select(m => new ProviderModel(m.Name))
            .ToList() ?? [];
    }

    /// <summary>Vertex has no simple list endpoint; verify the OAuth token mints, then return curated models.</summary>
    private async Task<IReadOnlyList<ProviderModel>> ListVertexAsync(ProviderConnection c, CancellationToken ct)
    {
        await _oauth.GetAccessTokenAsync(c, ct); // throws if not signed in / misconfigured
        return c.Models;
    }

    private static void ThrowIfAuthFailed(HttpStatusCode status)
    {
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedProbeException("The API key was rejected (check the key and the base URL).");
    }

    private sealed class OpenAiModelList
    {
        [JsonPropertyName("data")] public List<OpenAiModel>? Data { get; set; }
    }

    private sealed class OpenAiModel
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    }

    private sealed class OllamaTags
    {
        [JsonPropertyName("models")] public List<OllamaTag>? Models { get; set; }
    }

    private sealed class OllamaTag
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    private sealed class UnauthorizedProbeException(string message) : Exception(message);
}
