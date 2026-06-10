using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Providers;

namespace WindowsAI.Core.Chat;

/// <summary>
/// Central entry point for chat. Resolves the active provider/model from settings and
/// streams the reply token-by-token. Later phases grow the routing (local-first, cloud
/// opt-in) and the tool-calling loop here; for now it is a streaming pass-through.
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

    /// <summary>Streams the assistant reply for the given conversation as text deltas.</summary>
    public async IAsyncEnumerable<string> StreamReplyAsync(
        IEnumerable<ChatMessage> conversation,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var selection = _settings.Current.ActiveModel;
        var client = ActiveProvider.GetClient(selection.ModelId);
        var options = new ChatOptions { ModelId = selection.ModelId };

        await foreach (var update in client.GetStreamingResponseAsync(conversation, options, ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }
    }
}
