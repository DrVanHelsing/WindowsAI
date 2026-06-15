namespace WindowsAI.Core.Security;

/// <summary>Well-known keys for values stored in the <see cref="ISecretStore"/>.</summary>
public static class SecretKeys
{
    public const string OpenRouterApiKey = "openrouter.apikey";

    public const string GeminiApiKey = "gemini.apikey";

    /// <summary>OAuth client secret for the user's Google "Desktop app" client.</summary>
    public const string GoogleClientSecret = "google.clientsecret";

    /// <summary>Sentinel marking that a Google sign-in has completed (a refresh token is stored).</summary>
    public const string GoogleConnected = "google.connected";

    /// <summary>API key for Tavily Search, used by the <c>search_web</c> tool.</summary>
    public const string TavilyApiKey = "tavily.apikey";
}
