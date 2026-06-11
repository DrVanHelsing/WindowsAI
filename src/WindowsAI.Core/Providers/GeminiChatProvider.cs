using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Providers;

/// <summary>Google Gemini via its OpenAI-compatible endpoint. Opt-in (needs an API key).</summary>
public sealed class GeminiChatProvider : OpenAiCompatibleChatProvider
{
    public GeminiChatProvider(ISettingsStore settings, ISecretStore secrets)
        : base(settings, secrets) { }

    public override string Id => "gemini";
    public override string DisplayName => "Gemini (cloud)";

    /// <summary>Gemini models are multimodal — accept image attachments.</summary>
    public override bool SupportsVision => true;

    protected override string ApiKeyName => SecretKeys.GeminiApiKey;
    protected override string BaseUrl => Settings.Current.Gemini.BaseUrl;
}
