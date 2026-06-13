using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Answers questions about a captured screen region using a configurable OpenRouter vision model.
/// Like <see cref="ImageGenerationService"/>, this always runs on OpenRouter regardless of the
/// active chat model — visual intelligence needs a multimodal backend even when the user's
/// everyday model is a local text-only one. Each call is a fresh, self-contained exchange.
/// </summary>
public sealed class VisionService
{
    private const string SystemPrompt =
        "You are WindowsAI's visual assistant. The user captured a region of their screen and is " +
        "asking about it. Answer concisely and helpfully, describing exactly what is shown and " +
        "addressing their question directly.";

    private readonly IChatProvider _openRouter;
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;

    public VisionService(IEnumerable<IChatProvider> providers, ISettingsStore settings, ISecretStore secrets)
    {
        _openRouter = providers.First(p => p.Id == "openrouter");
        _settings = settings;
        _secrets = secrets;
    }

    /// <summary>True once an OpenRouter API key is present (the vision model is served there).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_secrets.Get(SecretKeys.OpenRouterApiKey));

    /// <summary>The model id used for vision requests (from settings).</summary>
    public string ModelId => _settings.Current.OpenRouter.VisionModelId;

    /// <summary>
    /// Streams the model's answer about <paramref name="imageBytes"/> token-by-token. A blank
    /// <paramref name="question"/> falls back to a general "describe this" prompt.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        byte[] imageBytes,
        string mediaType,
        string question,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new ProviderNotConfiguredException(
                "Visual intelligence uses OpenRouter — add an OpenRouter API key in Settings.");

        var prompt = string.IsNullOrWhiteSpace(question)
            ? "What's in this screenshot? Describe it and explain anything notable."
            : question.Trim();

        var modelId = ModelId;
        var client = _openRouter.GetClient(modelId);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, new List<AIContent>
            {
                new TextContent(prompt),
                new DataContent(imageBytes, mediaType),
            }),
        };

        var options = new ChatOptions { ModelId = modelId };
        await foreach (var update in client.GetStreamingResponseAsync(messages, options, ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }
    }
}
