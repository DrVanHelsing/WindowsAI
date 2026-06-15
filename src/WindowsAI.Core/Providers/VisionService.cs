using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WindowsAI.Core.Configuration;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Answers questions about a captured screen region using whichever configured provider can see
/// images. Visual intelligence needs a multimodal backend even when the user's everyday model is a
/// local text-only one, so this is independent of the active chat model: it prefers the active
/// provider when it's vision-capable, otherwise the first configured vision provider in priority
/// order (Gemini → Vertex → OpenRouter). Each call is a fresh, self-contained exchange.
/// </summary>
public sealed class VisionService
{
    private const string SystemPrompt =
        "You are WindowsAI's visual assistant. The user captured a region of their screen and is " +
        "asking about it. Answer concisely and helpfully, describing exactly what is shown and " +
        "addressing their question directly.";

    /// <summary>Vision providers in fallback priority order — Gemini is the user's default working key.</summary>
    private static readonly string[] VisionPriority = { "gemini", "vertex", "openrouter" };

    private readonly IReadOnlyList<IChatProvider> _providers;
    private readonly ISettingsStore _settings;

    public VisionService(IEnumerable<IChatProvider> providers, ISettingsStore settings)
    {
        _providers = providers.ToList();
        _settings = settings;
    }

    /// <summary>True when at least one vision-capable provider is configured.</summary>
    public bool IsConfigured => SelectProvider() is not null;

    /// <summary>The model id that would serve a vision request right now, or empty if none is configured.</summary>
    public string ModelId => SelectProvider() is { } p ? VisionModelFor(p) : string.Empty;

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
        var provider = SelectProvider()
            ?? throw new ProviderNotConfiguredException(
                "Visual intelligence needs a vision-capable provider. Add a Gemini API key (or sign in " +
                "with Google, or add an OpenRouter key) in Settings.");

        var prompt = string.IsNullOrWhiteSpace(question)
            ? "What's in this screenshot? Describe it and explain anything notable."
            : question.Trim();

        var modelId = VisionModelFor(provider);
        var client = provider.GetClient(modelId);

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

    /// <summary>
    /// Chooses the vision provider: the active provider if it's configured and multimodal, otherwise
    /// the first configured vision provider in <see cref="VisionPriority"/> order. Null if none qualify.
    /// </summary>
    private IChatProvider? SelectProvider()
    {
        var activeId = _settings.Current.ActiveModel.ProviderId;
        var active = _providers.FirstOrDefault(p => p.Id == activeId);
        if (active is { SupportsVision: true, IsConfigured: true })
            return active;

        foreach (var id in VisionPriority)
        {
            var provider = _providers.FirstOrDefault(p => p.Id == id);
            if (provider is { SupportsVision: true, IsConfigured: true })
                return provider;
        }

        return null;
    }

    /// <summary>The vision model id for a provider — they differ per backend.</summary>
    private string VisionModelFor(IChatProvider provider) => provider.Id switch
    {
        "gemini" => _settings.Current.Gemini.DefaultModelId,
        "vertex" => _settings.Current.GoogleCloud.DefaultModelId,
        "openrouter" => _settings.Current.OpenRouter.VisionModelId,
        // Any other configured multimodal provider: fall back to the active model id.
        _ => _settings.Current.ActiveModel.ModelId,
    };
}
