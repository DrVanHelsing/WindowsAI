using WindowsAI.Core.Providers;

namespace WindowsAI.Core.Configuration;

/// <summary>
/// Upgrades <see cref="AppSettings"/> in place to <see cref="AppSettings.CurrentSchemaVersion"/>.
/// A v1 file (fixed per-provider fields) is converted to data-driven <see cref="ProviderConnection"/>s
/// — reusing the original well-known ids so DPAPI-stored keys are preserved — and its scattered
/// model ids are mapped onto <see cref="CapabilityRouting"/>. A fresh install (no legacy data) is
/// seeded with just a local Ollama connection; the onboarding wizard adds the rest.
/// </summary>
public static class SettingsMigrator
{
    /// <summary>Returns true if anything changed (so the caller should persist).</summary>
    public static bool Migrate(AppSettings s)
    {
        if (s.SchemaVersion >= AppSettings.CurrentSchemaVersion)
            return false;

        var isV1 = s.LocalModel is not null || s.OpenRouter is not null || s.Gemini is not null
                || s.GoogleCloud is not null || s.ActiveModel is not null;

        if (isV1)
            MigrateFromV1(s);
        else
            SeedFresh(s);

        s.SchemaVersion = AppSettings.CurrentSchemaVersion;

        // Drop legacy payloads so they don't linger in the saved file.
        s.ActiveModel = null;
        s.LocalModel = null;
        s.OpenRouter = null;
        s.Gemini = null;
        s.GoogleCloud = null;
        s.WebSearch = null;
        return true;
    }

    private static void SeedFresh(AppSettings s)
    {
        var ollama = ProviderPresets.ByKey("ollama")!.ToConnection("ollama");
        s.Providers.Add(ollama);

        s.Capabilities.Chat = new ModelSelection("ollama", ollama.DefaultModelId ?? "gemma4:e2b-it-qat");
        s.Capabilities.Embeddings = new ModelSelection("ollama", "nomic-embed-text");
    }

    private static void MigrateFromV1(AppSettings s)
    {
        var local = s.LocalModel ?? new LocalModelSettings();
        var openRouter = s.OpenRouter ?? new OpenRouterSettings();
        var gemini = s.Gemini ?? new GeminiSettings();
        var google = s.GoogleCloud ?? new GoogleCloudSettings();

        // ── Ollama (local) ──
        var ollama = ProviderPresets.ByKey("ollama")!.ToConnection("ollama");
        ollama.BaseUrl = local.Endpoint;
        ollama.KeepAlive = local.KeepAlive;
        ollama.DefaultModelId = local.DefaultModelId;
        ollama.Models = new List<ProviderModel> { new(local.DefaultModelId) };
        s.Providers.Add(ollama);

        // ── OpenRouter ──
        var or = ProviderPresets.ByKey("openrouter")!.ToConnection("openrouter");
        or.BaseUrl = openRouter.BaseUrl;
        or.DefaultModelId = openRouter.DefaultModelId;
        or.Models = Distinct(openRouter.DefaultModelId, openRouter.VisionModelId, openRouter.ImageModelId);
        s.Providers.Add(or);

        // ── Gemini (API key) ──
        var gem = ProviderPresets.ByKey("gemini")!.ToConnection("gemini");
        gem.BaseUrl = gemini.BaseUrl;
        gem.DefaultModelId = gemini.DefaultModelId;
        gem.Models = Distinct(gemini.DefaultModelId);
        s.Providers.Add(gem);

        // ── Vertex (Google sign-in) ──
        var vertex = ProviderPresets.ByKey("vertex")!.ToConnection("vertex");
        vertex.OAuthClientId = google.ClientId;
        vertex.ProjectId = google.ProjectId;
        vertex.Location = google.Location;
        vertex.DefaultModelId = google.DefaultModelId;
        vertex.Models = Distinct(google.DefaultModelId);
        s.Providers.Add(vertex);

        // ── Capabilities ──
        s.Capabilities.Chat = s.ActiveModel ?? new ModelSelection("ollama", local.DefaultModelId);
        s.Capabilities.ImageGeneration = new ModelSelection("openrouter", openRouter.ImageModelId);
        s.Capabilities.Embeddings = new ModelSelection("ollama", local.EmbeddingModelId);
        // VisualIntelligence stays null → router auto-picks the first configured vision provider.

        // ── Features ──
        s.Features.WebSearch = s.WebSearch?.Enabled ?? true;
    }

    private static List<ProviderModel> Distinct(params string?[] modelIds) =>
        modelIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new ProviderModel(id))
            .ToList();
}
