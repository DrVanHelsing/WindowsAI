using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Providers;

/// <summary>
/// A chat backend reached over the OpenAI chat-completions protocol, configured entirely from a
/// user-defined <see cref="ProviderConnection"/> (base URL + API key + models). Covers OpenAI,
/// OpenRouter, Gemini's OpenAI-compatible endpoint, Anthropic's compat endpoint, Groq, DeepSeek,
/// local LM Studio / llama.cpp / vLLM, and anything else that speaks the same protocol.
/// </summary>
public sealed class OpenAiConnectionProvider : IChatProvider
{
    private readonly ProviderConnection _connection;
    private readonly ISecretStore _secrets;

    public OpenAiConnectionProvider(ProviderConnection connection, ISecretStore secrets)
    {
        _connection = connection;
        _secrets = secrets;
    }

    public string Id => _connection.Id;
    public string DisplayName => _connection.DisplayName;
    public bool IsLocal => IsLocalEndpoint(_connection.BaseUrl);

    /// <summary>Local OpenAI-compatible servers (LM Studio, llama.cpp) accept requests without a real key.</summary>
    public bool RequiresApiKey => !IsLocal;

    public bool SupportsVision => _connection.SupportsVision;
    public bool SupportsImageGeneration => _connection.SupportsImageGeneration;
    public string? DefaultModelId => _connection.DefaultModelId;

    private string ApiKeyName => SecretKeys.ApiKeyFor(_connection.Id);

    public bool IsConfigured =>
        !RequiresApiKey || !string.IsNullOrWhiteSpace(_secrets.Get(ApiKeyName));

    public IChatClient GetClient(string modelId)
    {
        var key = _secrets.Get(ApiKeyName);
        if (RequiresApiKey && string.IsNullOrWhiteSpace(key))
            throw new ProviderNotConfiguredException($"{DisplayName} needs an API key. Add it in Settings.");

        var options = new OpenAIClientOptions { Endpoint = new Uri(_connection.BaseUrl) };
        // Local key-less servers still require a non-empty credential string; any value works.
        var credential = new ApiKeyCredential(string.IsNullOrWhiteSpace(key) ? "local" : key);
        var client = new OpenAIClient(credential, options);
        return client.GetChatClient(modelId).AsIChatClient();
    }

    private static bool IsLocalEndpoint(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
}
