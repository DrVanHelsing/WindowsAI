using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Answers questions about a captured screen region using the model bound to the
/// <see cref="Capability.VisualIntelligence"/> capability — an explicit pick if the user set one,
/// otherwise the first configured vision-capable provider (resolved by <see cref="CapabilityRouter"/>).
/// Independent of the everyday chat model, so visual intelligence works even when chat runs on a
/// local text-only model. Each call is a fresh, self-contained exchange.
/// </summary>
public sealed class VisionService
{
    private const string SystemPrompt =
        "You are WindowsAI's visual assistant. The user captured a region of their screen and is " +
        "asking about it. Answer concisely and helpfully, describing exactly what is shown and " +
        "addressing their question directly. " +
        "Format your answer in Markdown so structure is preserved: when the region contains a table " +
        "or spreadsheet, reproduce it as a Markdown table (| header | … | with a |---| separator row); " +
        "render lists as Markdown lists, key/value pairs as a short list or two-column table, and code " +
        "or terminal output in fenced code blocks. Transcribe text, numbers, and labels faithfully — " +
        "do not invent or omit cells. Keep prose minimal when the user just wants the data extracted.";

    private readonly CapabilityRouter _router;

    public VisionService(CapabilityRouter router) => _router = router;

    /// <summary>True when a vision-capable provider is configured.</summary>
    public bool IsConfigured => _router.Resolve(Capability.VisualIntelligence) is not null;

    /// <summary>The model id that would serve a vision request right now, or empty if none is configured.</summary>
    public string ModelId => _router.Resolve(Capability.VisualIntelligence)?.ModelId ?? string.Empty;

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
        var resolved = _router.Resolve(Capability.VisualIntelligence)
            ?? throw new ProviderNotConfiguredException(
                "Visual intelligence needs a vision-capable provider. Add one (e.g. Gemini, OpenRouter, " +
                "or sign in with Google) in Settings → Models & providers, then pick it under Visual Intelligence.");

        var prompt = string.IsNullOrWhiteSpace(question)
            ? "What's in this screenshot? Describe it and explain anything notable."
            : question.Trim();

        var client = resolved.Provider.GetClient(resolved.ModelId);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, new List<AIContent>
            {
                new TextContent(prompt),
                new DataContent(imageBytes, mediaType),
            }),
        };

        var options = new ChatOptions { ModelId = resolved.ModelId };
        await foreach (var update in client.GetStreamingResponseAsync(messages, options, ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }
    }
}
