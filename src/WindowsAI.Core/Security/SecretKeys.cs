namespace WindowsAI.Core.Security;

/// <summary>
/// Well-known keys for values stored in the <see cref="ISecretStore"/>, plus per-connection key
/// derivation. Built-in seeded connections reuse the original v1 key names (so a migrated user
/// keeps their stored keys); user-added connections get a key derived from their connection id.
/// </summary>
public static class SecretKeys
{
    // ── Legacy / built-in well-known names ──
    public const string OpenRouterApiKey = "openrouter.apikey";
    public const string GeminiApiKey = "gemini.apikey";

    /// <summary>OAuth client secret for the user's Google "Desktop app" client (the seeded "vertex" connection).</summary>
    public const string GoogleClientSecret = "google.clientsecret";

    /// <summary>Sentinel marking that a Google sign-in has completed (a refresh token is stored).</summary>
    public const string GoogleConnected = "google.connected";

    /// <summary>API key for Tavily Search, used by the <c>search_web</c> tool.</summary>
    public const string TavilyApiKey = "tavily.apikey";

    /// <summary>The DPAPI key holding the API key for a provider connection.</summary>
    public static string ApiKeyFor(string connectionId) => connectionId switch
    {
        "openrouter" => OpenRouterApiKey,
        "gemini" => GeminiApiKey,
        _ => $"provider.{connectionId}.apikey",
    };

    /// <summary>The DPAPI key holding the OAuth client secret for a (Vertex) provider connection.</summary>
    public static string ClientSecretFor(string connectionId) => connectionId switch
    {
        "vertex" => GoogleClientSecret,
        _ => $"provider.{connectionId}.clientsecret",
    };

    /// <summary>The DPAPI key prefix for a connection's stored OAuth tokens.</summary>
    public static string OAuthPrefixFor(string connectionId) =>
        connectionId == "vertex" ? "google.oauth." : $"provider.{connectionId}.oauth.";

    /// <summary>The DPAPI sentinel marking that a connection's Google sign-in has completed.</summary>
    public static string ConnectedFor(string connectionId) =>
        connectionId == "vertex" ? GoogleConnected : $"provider.{connectionId}.oauth.connected";
}
