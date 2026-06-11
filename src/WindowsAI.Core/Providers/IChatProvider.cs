using Microsoft.Extensions.AI;

namespace WindowsAI.Core.Providers;

/// <summary>
/// A chat backend (local or cloud). Each provider exposes a Microsoft.Extensions.AI
/// <see cref="IChatClient"/> so the orchestrator stays backend-agnostic.
/// </summary>
public interface IChatProvider
{
    /// <summary>Stable identifier persisted in settings (e.g. "ollama", "openrouter", "gemini").</summary>
    string Id { get; }

    /// <summary>Human-friendly name for the UI.</summary>
    string DisplayName { get; }

    /// <summary>True if inference runs on-device (drives the local-first routing/privacy posture).</summary>
    bool IsLocal { get; }

    /// <summary>True if the provider needs an API key before it can be used.</summary>
    bool RequiresApiKey { get; }

    /// <summary>True if the provider is ready to use (e.g. its API key has been entered).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// True if the active model can accept images (multimodal). Defaults to false so text-only
    /// backends (e.g. local Gemma via Ollama) are never sent image content they'd reject.
    /// </summary>
    bool SupportsVision => false;

    /// <summary>
    /// Returns a chat client for the given model id, configured from current settings and secrets.
    /// Throws <see cref="ProviderNotConfiguredException"/> if a required key is missing.
    /// </summary>
    IChatClient GetClient(string modelId);
}

/// <summary>Thrown when a provider is selected but not yet configured (e.g. missing API key).</summary>
public sealed class ProviderNotConfiguredException(string message) : InvalidOperationException(message);
