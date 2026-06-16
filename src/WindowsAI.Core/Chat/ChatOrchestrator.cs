using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WindowsAI.Core.Providers;

namespace WindowsAI.Core.Chat;

/// <summary>
/// Central entry point for chat. Resolves the model for a capability (chat by default) via the
/// <see cref="CapabilityRouter"/> and streams the reply token-by-token. When <paramref name="tools"/>
/// are supplied it runs an agent tool-calling loop (model → tool → model …) via
/// Microsoft.Extensions.AI's <c>FunctionInvokingChatClient</c>; without tools it is a plain
/// streaming pass-through.
/// </summary>
public sealed class ChatOrchestrator
{
    private readonly CapabilityRouter _router;

    public ChatOrchestrator(CapabilityRouter router) => _router = router;

    /// <summary>The provider serving the currently selected chat model, or null if none is configured.</summary>
    public IChatProvider? ActiveProvider => _router.Resolve(Capability.Chat)?.Provider;

    /// <summary>A short label for the active chat model, e.g. "gemma4:e2b-it-qat · Ollama (local)".</summary>
    public string ActiveModelLabel
    {
        get
        {
            var resolved = _router.Resolve(Capability.Chat);
            return resolved is null
                ? "No model configured"
                : $"{resolved.ModelId} · {resolved.Provider.DisplayName}";
        }
    }

    /// <summary>
    /// Streams the assistant reply for the given conversation as text deltas, using the model bound
    /// to <paramref name="capability"/>. If <paramref name="tools"/> are provided the model may call
    /// them; tool invocation and follow-up turns are handled internally and only text is yielded.
    /// </summary>
    public async IAsyncEnumerable<string> StreamReplyAsync(
        IEnumerable<ChatMessage> conversation,
        IReadOnlyList<AITool>? tools = null,
        Capability capability = Capability.Chat,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var resolved = _router.Resolve(capability)
            ?? throw new ProviderNotConfiguredException(
                "No model is configured. Add a provider in Settings → Models & providers.");

        var modelId = resolved.ModelId;

        if (tools is not { Count: > 0 })
        {
            // No tools: plain streaming pass-through.
            var plain = new ChatOptions { ModelId = modelId };
            await foreach (var delta in StreamTextAsync(resolved.Provider.GetClient(modelId), conversation, plain, ct))
                yield return delta;
            yield break;
        }

        // Tool-calling loop. Some local models (e.g. Gemma via Ollama) reject requests that
        // carry tools; that rejection arrives before any text streams, so if the first token
        // throws we retry once without tools rather than failing the whole message.
        var agentClient = resolved.Provider.GetClient(modelId)
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();
        var options = new ChatOptions { ModelId = modelId, Tools = tools.ToList() };

        await using var enumerator = agentClient
            .GetStreamingResponseAsync(conversation, options, ct)
            .GetAsyncEnumerator(ct);

        var streamedAnyText = false;
        var toolsRejected = false;
        while (true)
        {
            bool moved = false;
            string? delta = null;
            try
            {
                moved = await enumerator.MoveNextAsync();
                if (moved && !string.IsNullOrEmpty(enumerator.Current.Text))
                    delta = enumerator.Current.Text;
            }
            catch (Exception ex) when (!streamedAnyText && ex is not OperationCanceledException)
            {
                // Model/provider doesn't support tools — break out and retry without them.
                toolsRejected = true;
            }

            if (toolsRejected)
                break;

            if (!moved)
                yield break;

            if (delta is not null)
            {
                streamedAnyText = true;
                yield return delta;
            }
        }

        // Fallback: stream a plain (tool-free) reply for models that rejected the tools.
        var fallbackOptions = new ChatOptions { ModelId = modelId };
        await foreach (var d in StreamTextAsync(resolved.Provider.GetClient(modelId), conversation, fallbackOptions, ct))
            yield return d;
    }

    /// <summary>Streams text deltas from a client with the given (optional) options.</summary>
    private static async IAsyncEnumerable<string> StreamTextAsync(
        IChatClient client,
        IEnumerable<ChatMessage> conversation,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        options ??= new ChatOptions();
        await foreach (var update in client.GetStreamingResponseAsync(conversation, options, ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }
    }
}
