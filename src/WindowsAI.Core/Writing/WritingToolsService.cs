using System.Text;
using WindowsAI.Core.Chat;
using WindowsAI.Core.Providers;

namespace WindowsAI.Core.Writing;

/// <summary>
/// Runs a writing-tools transform against the active model. It owns no UI and no OS state — it
/// takes a selection plus an action and streams back the transformed text — so the same service
/// drives the type-anywhere flyout today and could drive an in-app menu later. The transform is
/// a one-shot exchange (no tools, no history): each call builds a fresh, self-contained prompt.
/// </summary>
public sealed class WritingToolsService
{
    private readonly ChatOrchestrator _orchestrator;

    public WritingToolsService(ChatOrchestrator orchestrator) => _orchestrator = orchestrator;

    /// <summary>Streams the transformed text token-by-token (raw deltas, not yet cleaned).</summary>
    public IAsyncEnumerable<string> StreamAsync(
        WritingAction action, string selection, CancellationToken ct = default)
    {
        var prompt = WritingPrompt.Build(action, selection);
        return _orchestrator.StreamReplyAsync(prompt, tools: null, capability: Capability.WritingTools, ct: ct);
    }

    /// <summary>
    /// Streams the model's response to a free-form user <paramref name="request"/> about the
    /// <paramref name="selection"/> (the "Ask" flow), token-by-token (raw deltas, not yet cleaned).
    /// </summary>
    public IAsyncEnumerable<string> StreamAskAsync(
        string request, string selection, CancellationToken ct = default)
    {
        var prompt = WritingPrompt.BuildAsk(request, selection);
        return _orchestrator.StreamReplyAsync(prompt, tools: null, capability: Capability.WritingTools, ct: ct);
    }

    /// <summary>
    /// Runs the transform to completion and returns the cleaned result ready to inject. Throws
    /// if the selection is blank (nothing to transform).
    /// </summary>
    public async Task<string> TransformAsync(
        WritingAction action, string selection, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(selection))
            throw new ArgumentException("Nothing is selected.", nameof(selection));

        var sb = new StringBuilder();
        await foreach (var delta in StreamAsync(action, selection, ct))
            sb.Append(delta);

        return WritingPrompt.CleanResult(sb.ToString());
    }
}
