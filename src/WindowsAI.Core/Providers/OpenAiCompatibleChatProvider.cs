using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Base for cloud providers that speak the OpenAI chat-completions protocol
/// (OpenRouter, Gemini's OpenAI-compatible endpoint, etc.). Subclasses supply the
/// base URL and which secret holds the API key.
/// </summary>
public abstract class OpenAiCompatibleChatProvider : IChatProvider
{
    protected OpenAiCompatibleChatProvider(ISettingsStore settings, ISecretStore secrets)
    {
        Settings = settings;
        Secrets = secrets;
    }

    protected ISettingsStore Settings { get; }
    protected ISecretStore Secrets { get; }

    public abstract string Id { get; }
    public abstract string DisplayName { get; }

    public bool IsLocal => false;
    public bool RequiresApiKey => true;

    /// <summary>Whether this endpoint's models accept images. Off by default; vision-capable
    /// cloud providers (Gemini, OpenRouter) override it.</summary>
    public virtual bool SupportsVision => false;

    /// <summary>Name of the secret (in <see cref="ISecretStore"/>) holding this provider's API key.</summary>
    protected abstract string ApiKeyName { get; }

    /// <summary>Base URL of the OpenAI-compatible endpoint.</summary>
    protected abstract string BaseUrl { get; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Secrets.Get(ApiKeyName));

    public IChatClient GetClient(string modelId)
    {
        var key = Secrets.Get(ApiKeyName);
        if (string.IsNullOrWhiteSpace(key))
            throw new ProviderNotConfiguredException($"{DisplayName} needs an API key. Add it in Settings.");

        var options = new OpenAIClientOptions { Endpoint = new Uri(BaseUrl) };
        var client = new OpenAIClient(new ApiKeyCredential(key), options);
        return client.GetChatClient(modelId).AsIChatClient();
    }
}
