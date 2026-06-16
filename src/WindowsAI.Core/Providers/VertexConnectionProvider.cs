using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using WindowsAI.Core.Auth;
using WindowsAI.Core.Configuration;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Gemini through Vertex AI's OpenAI-compatible endpoint, authenticated with a Google sign-in
/// (OAuth) instead of an API key, configured from a <see cref="ProviderConnection"/>
/// (project id + region + OAuth client id). The bearer token is a short-lived OAuth access token
/// minted by <see cref="GoogleOAuthService"/>; the endpoint is scoped to the connection's project.
/// </summary>
public sealed class VertexConnectionProvider : IChatProvider
{
    private readonly ProviderConnection _connection;
    private readonly GoogleOAuthService _auth;

    public VertexConnectionProvider(ProviderConnection connection, GoogleOAuthService auth)
    {
        _connection = connection;
        _auth = auth;
    }

    public string Id => _connection.Id;
    public string DisplayName => _connection.DisplayName;
    public bool IsLocal => false;

    /// <summary>Auth is via Google sign-in, not a pasted API key.</summary>
    public bool RequiresApiKey => false;

    /// <summary>Gemini models are multimodal — accept image attachments.</summary>
    public bool SupportsVision => _connection.SupportsVision;
    public string? DefaultModelId => _connection.DefaultModelId;

    public bool IsConfigured =>
        _auth.IsSignedIn(_connection)
        && !string.IsNullOrWhiteSpace(_connection.ProjectId)
        && !string.IsNullOrWhiteSpace(_connection.Location);

    public IChatClient GetClient(string modelId)
    {
        if (string.IsNullOrWhiteSpace(_connection.ProjectId) || string.IsNullOrWhiteSpace(_connection.Location))
            throw new ProviderNotConfiguredException(
                "Vertex AI needs a project id and region. Set them on the provider's card.");

        // Mint a fresh access token (the credential refreshes itself when near expiry). GetClient
        // is synchronous, so block briefly here; the call only hits the network around expiry.
        string token;
        try
        {
            token = _auth.GetAccessTokenAsync(_connection).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            throw new ProviderNotConfiguredException(ex.Message);
        }

        var options = new OpenAIClientOptions { Endpoint = new Uri(BuildEndpoint(_connection)) };
        var client = new OpenAIClient(new ApiKeyCredential(token), options);
        return client.GetChatClient(modelId).AsIChatClient();
    }

    /// <summary>
    /// Vertex's OpenAI-compatible base URL. The global endpoint drops the regional host prefix;
    /// every other region uses <c>{region}-aiplatform.googleapis.com</c>.
    /// </summary>
    private static string BuildEndpoint(ProviderConnection c)
    {
        var location = c.Location!;
        var host = string.Equals(location, "global", StringComparison.OrdinalIgnoreCase)
            ? "aiplatform.googleapis.com"
            : $"{location}-aiplatform.googleapis.com";
        return $"https://{host}/v1beta1/projects/{c.ProjectId}/locations/{location}/endpoints/openapi";
    }
}
