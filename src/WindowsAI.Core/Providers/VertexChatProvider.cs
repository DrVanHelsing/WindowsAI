using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using WindowsAI.Core.Auth;
using WindowsAI.Core.Configuration;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Gemini through Vertex AI's OpenAI-compatible endpoint, authenticated with a Google sign-in
/// (OAuth) instead of an API key. The bearer token is a short-lived OAuth access token minted by
/// <see cref="GoogleOAuthService"/>; the endpoint is scoped to the user's project + region.
/// </summary>
public sealed class VertexChatProvider : IChatProvider
{
    private readonly GoogleOAuthService _auth;
    private readonly ISettingsStore _settings;

    public VertexChatProvider(GoogleOAuthService auth, ISettingsStore settings)
    {
        _auth = auth;
        _settings = settings;
    }

    public string Id => "vertex";
    public string DisplayName => "Gemini (Google sign-in)";

    public bool IsLocal => false;

    /// <summary>Auth is via Google sign-in, not a pasted API key.</summary>
    public bool RequiresApiKey => false;

    /// <summary>Gemini models are multimodal — accept image attachments.</summary>
    public bool SupportsVision => true;

    public bool IsConfigured
    {
        get
        {
            var g = _settings.Current.GoogleCloud;
            return _auth.IsSignedIn
                && !string.IsNullOrWhiteSpace(g.ProjectId)
                && !string.IsNullOrWhiteSpace(g.Location);
        }
    }

    public IChatClient GetClient(string modelId)
    {
        var g = _settings.Current.GoogleCloud;
        if (string.IsNullOrWhiteSpace(g.ProjectId) || string.IsNullOrWhiteSpace(g.Location))
            throw new ProviderNotConfiguredException(
                "Vertex AI needs a project id and region. Set them in Settings → Google sign-in.");

        // Mint a fresh access token (the credential refreshes itself when near expiry). GetClient
        // is synchronous, so block briefly here; the call only hits the network around expiry.
        string token;
        try
        {
            token = _auth.GetAccessTokenAsync().GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            throw new ProviderNotConfiguredException(ex.Message);
        }

        var options = new OpenAIClientOptions { Endpoint = new Uri(BuildEndpoint(g)) };
        var client = new OpenAIClient(new ApiKeyCredential(token), options);
        return client.GetChatClient(modelId).AsIChatClient();
    }

    /// <summary>
    /// Vertex's OpenAI-compatible base URL. The global endpoint drops the regional host prefix;
    /// every other region uses <c>{region}-aiplatform.googleapis.com</c>.
    /// </summary>
    private static string BuildEndpoint(GoogleCloudSettings g)
    {
        var host = string.Equals(g.Location, "global", StringComparison.OrdinalIgnoreCase)
            ? "aiplatform.googleapis.com"
            : $"{g.Location}-aiplatform.googleapis.com";
        return $"https://{host}/v1beta1/projects/{g.ProjectId}/locations/{g.Location}/endpoints/openapi";
    }
}
