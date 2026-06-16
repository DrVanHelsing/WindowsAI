using WindowsAI.Core.Configuration;

namespace WindowsAI.Core.Providers;

/// <summary>An app capability that routes to a model on a provider connection.</summary>
public enum Capability
{
    Chat,
    VisualIntelligence,
    WritingTools,
    ImageGeneration,
    Embeddings,
}

/// <summary>A resolved capability target: the provider to use and the model id to request.</summary>
public sealed record ResolvedModel(IChatProvider Provider, string ModelId);

/// <summary>
/// Resolves which configured model serves each capability, applying fallbacks when a capability
/// doesn't pin its own model: writing → chat; vision/image → the first configured capable provider;
/// embeddings → only if explicitly set. Replaces the old hardcoded vision-priority logic with a
/// fully data-driven lookup over the <see cref="IProviderRegistry"/>.
/// </summary>
public sealed class CapabilityRouter
{
    private readonly ISettingsStore _settings;
    private readonly IProviderRegistry _registry;

    public CapabilityRouter(ISettingsStore settings, IProviderRegistry registry)
    {
        _settings = settings;
        _registry = registry;
    }

    /// <summary>Resolves the provider + model for a capability, or null if nothing suitable is configured.</summary>
    public ResolvedModel? Resolve(Capability capability)
    {
        var caps = _settings.Current.Capabilities;
        return capability switch
        {
            Capability.Chat =>
                FromSelection(caps.Chat, requireVision: false, requireImage: false)
                ?? FirstConfigured(requireVision: false, requireImage: false),

            Capability.WritingTools =>
                FromOptional(caps.WritingTools, requireVision: false, requireImage: false)
                ?? Resolve(Capability.Chat),

            Capability.VisualIntelligence =>
                FromOptional(caps.VisualIntelligence, requireVision: true, requireImage: false)
                ?? FirstConfigured(requireVision: true, requireImage: false),

            Capability.ImageGeneration =>
                FromOptional(caps.ImageGeneration, requireVision: false, requireImage: true)
                ?? FirstConfigured(requireVision: false, requireImage: true),

            // Embeddings have no auto-fallback — they only work when explicitly configured.
            Capability.Embeddings =>
                FromOptional(caps.Embeddings, requireVision: false, requireImage: false),

            _ => null,
        };
    }

    /// <summary>Resolves an optional (nullable) selection, returning null when none is set.</summary>
    private ResolvedModel? FromOptional(ModelSelection? selection, bool requireVision, bool requireImage)
        => selection is null || selection.IsEmpty ? null : FromSelection(selection, requireVision, requireImage);

    /// <summary>Resolves a concrete selection if its provider is configured and matches the requirements.</summary>
    private ResolvedModel? FromSelection(ModelSelection selection, bool requireVision, bool requireImage)
    {
        if (selection.IsEmpty)
            return null;

        var provider = _registry.GetConfigured(selection.ProviderId);
        if (provider is null || !Matches(provider, requireVision, requireImage))
            return null;

        var modelId = !string.IsNullOrWhiteSpace(selection.ModelId)
            ? selection.ModelId
            : provider.DefaultModelId;
        return string.IsNullOrWhiteSpace(modelId) ? null : new ResolvedModel(provider, modelId);
    }

    /// <summary>The first configured provider matching the requirements, using its default model.</summary>
    private ResolvedModel? FirstConfigured(bool requireVision, bool requireImage)
    {
        foreach (var provider in _registry.All)
        {
            if (!provider.IsConfigured || !Matches(provider, requireVision, requireImage))
                continue;
            if (!string.IsNullOrWhiteSpace(provider.DefaultModelId))
                return new ResolvedModel(provider, provider.DefaultModelId!);
        }
        return null;
    }

    private static bool Matches(IChatProvider provider, bool requireVision, bool requireImage)
        => (!requireVision || provider.SupportsVision)
        && (!requireImage || provider.SupportsImageGeneration);
}
