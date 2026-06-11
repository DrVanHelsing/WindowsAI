using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Providers;

namespace WindowsAI.Core.Chat;

/// <summary>
/// Central entry point for chat. Resolves the active provider/model from settings and
/// streams the reply token-by-token. When <paramref name="tools"/> are supplied it runs an
/// agent tool-calling loop (model → tool → model …) via Microsoft.Extensions.AI's
/// <c>FunctionInvokingChatClient</c>; without tools it is a plain streaming pass-through.
/// </summary>
public sealed class ChatOrchestrator
{
    private readonly IReadOnlyList<IChatProvider> _providers;
    private readonly ISettingsStore _settings;

    public ChatOrchestrator(IEnumerable<IChatProvider> providers, ISettingsStore settings)
    {
        _providers = providers.ToList();
        _settings = settings;
    }

    /// <summary>The provider serving the currently selected model (falls back to the first registered).</summary>
    public IChatProvider ActiveProvider =>
        _providers.FirstOrDefault(p => p.Id == _settings.Current.ActiveModel.ProviderId)
        ?? _providers[0];

    /// <summary>A short label for the active model, e.g. "gemma4:e2b-it-qat · Ollama (local)".</summary>
    public string ActiveModelLabel => $"{_settings.Current.ActiveModel.ModelId} · {ActiveProvider.DisplayName}";

    /// <summary>
    /// Streams the assistant reply for the given conversation as text deltas. If
    /// <paramref name="tools"/> are provided the model may call them; tool invocation and the
    /// follow-up turns are handled internally and only assistant text is yielded.
    /// </summary>
    public async IAsyncEnumerable<string> StreamReplyAsync(
        IEnumerable<ChatMessage> conversation,
        IReadOnlyList<AITool>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var selection = _settings.Current.ActiveModel;

        if (tools is not { Count: > 0 })
        {
            // No tools: plain streaming pass-through.
            var plain = new ChatOptions { ModelId = selection.ModelId };
            await foreach (var delta in StreamTextAsync(ActiveProvider.GetClient(selection.ModelId), conversation, plain, ct))
                yield return delta;
            yield break;
        }

        // Tool-calling loop. Some local models (e.g. Gemma via Ollama) reject requests that
        // carry tools; that rejection arrives before any text streams, so if the first token
        // throws we retry once without tools rather than failing the whole message.
        var agentClient = ActiveProvider.GetClient(selection.ModelId)
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();
        var options = new ChatOptions { ModelId = selection.ModelId, Tools = tools.ToList() };

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
                // (yield can't appear inside a catch, so the fallback runs after the loop.)
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
        var fallbackOptions = new ChatOptions { ModelId = selection.ModelId };
        await foreach (var d in StreamTextAsync(ActiveProvider.GetClient(selection.ModelId), conversation, fallbackOptions, ct))
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
