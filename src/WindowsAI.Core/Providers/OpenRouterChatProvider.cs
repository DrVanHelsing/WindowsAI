using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Providers;

/// <summary>OpenRouter cloud provider (gateway to many models, incl. free ones). Opt-in.</summary>
public sealed class OpenRouterChatProvider : OpenAiCompatibleChatProvider
{
    public OpenRouterChatProvider(ISettingsStore settings, ISecretStore secrets)
        : base(settings, secrets) { }

    public override string Id => "openrouter";
    public override string DisplayName => "OpenRouter (cloud)";

    /// <summary>OpenRouter gateways many vision-capable models — allow image attachments.</summary>
    public override bool SupportsVision => true;

    protected override string ApiKeyName => SecretKeys.OpenRouterApiKey;
    protected override string BaseUrl => Settings.Current.OpenRouter.BaseUrl;
}
