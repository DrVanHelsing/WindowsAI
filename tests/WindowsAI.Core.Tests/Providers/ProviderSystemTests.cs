using WindowsAI.Core.Auth;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Providers;
using WindowsAI.Core.Security;
using Xunit;

namespace WindowsAI.Core.Tests.Providers;

public class ProviderSystemTests
{
    // ── SettingsMigrator ──────────────────────────────────────────────────────

    [Fact]
    public void Migrate_fresh_install_seeds_a_local_ollama_provider_and_chat_model()
    {
        var s = new AppSettings(); // SchemaVersion 0, no legacy data

        var changed = SettingsMigrator.Migrate(s);

        Assert.True(changed);
        Assert.Equal(AppSettings.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Contains(s.Providers, p => p.Id == "ollama" && p.Kind == ProviderKind.Ollama);
        Assert.Equal("ollama", s.Capabilities.Chat.ProviderId);
    }

    [Fact]
    public void Migrate_v1_preserves_known_provider_ids_and_maps_capabilities()
    {
        var s = new AppSettings
        {
            ActiveModel = new ModelSelection("openrouter", "router-model"),
            LocalModel = new LocalModelSettings { Endpoint = "http://host:1234", DefaultModelId = "m", EmbeddingModelId = "embed-x" },
            OpenRouter = new OpenRouterSettings { ImageModelId = "image-x" },
            Gemini = new GeminiSettings(),
            GoogleCloud = new GoogleCloudSettings { ClientId = "cid", ProjectId = "pid", Location = "us-central1" },
            WebSearch = new WebSearchSettings { Enabled = false },
        };

        SettingsMigrator.Migrate(s);

        Assert.Contains(s.Providers, p => p.Id == "ollama" && p.BaseUrl == "http://host:1234");
        Assert.Contains(s.Providers, p => p.Id == "openrouter");
        Assert.Contains(s.Providers, p => p.Id == "gemini");
        Assert.Contains(s.Providers, p => p is { Id: "vertex", OAuthClientId: "cid", ProjectId: "pid", Location: "us-central1" });

        Assert.Equal("openrouter", s.Capabilities.Chat.ProviderId);
        Assert.Equal("image-x", s.Capabilities.ImageGeneration!.ModelId);
        Assert.Equal("embed-x", s.Capabilities.Embeddings!.ModelId);
        Assert.False(s.Features.WebSearch);

        // Legacy payloads are dropped after migration.
        Assert.Null(s.LocalModel);
        Assert.Null(s.OpenRouter);
        Assert.Null(s.GoogleCloud);
    }

    [Fact]
    public void Migrate_is_idempotent_once_at_current_version()
    {
        var s = new AppSettings();
        Assert.True(SettingsMigrator.Migrate(s));
        Assert.False(SettingsMigrator.Migrate(s));
    }

    // ── SecretKeys derivation ─────────────────────────────────────────────────

    [Fact]
    public void SecretKeys_reuse_legacy_names_for_builtins_and_derive_for_custom()
    {
        Assert.Equal("openrouter.apikey", SecretKeys.ApiKeyFor("openrouter"));
        Assert.Equal("gemini.apikey", SecretKeys.ApiKeyFor("gemini"));
        Assert.Equal("provider.custom-abc.apikey", SecretKeys.ApiKeyFor("custom-abc"));
        Assert.Equal("google.clientsecret", SecretKeys.ClientSecretFor("vertex"));
        Assert.Equal("google.connected", SecretKeys.ConnectedFor("vertex"));
        Assert.Equal("provider.my-vertex.oauth.connected", SecretKeys.ConnectedFor("my-vertex"));
    }

    // ── ProviderRegistry ──────────────────────────────────────────────────────

    [Fact]
    public void Registry_surfaces_only_enabled_providers()
    {
        var settings = new FakeSettingsStore();
        settings.Current.Providers.Add(Cloud("a", "https://a.example/v1"));
        settings.Current.Providers.Add(new ProviderConnection
        {
            Id = "b", DisplayName = "B", Kind = ProviderKind.Ollama,
            BaseUrl = "http://localhost:11434", Enabled = false,
        });

        var registry = NewRegistry(settings, new FakeSecretStore());

        Assert.Single(registry.All);
        Assert.Equal("a", registry.All[0].Id);
        Assert.Null(registry.GetById("b"));
    }

    [Fact]
    public void Registry_GetConfigured_requires_a_key_for_cloud_then_succeeds()
    {
        var settings = new FakeSettingsStore();
        settings.Current.Providers.Add(Cloud("openrouter", "https://openrouter.ai/api/v1"));
        var secrets = new FakeSecretStore();
        var registry = NewRegistry(settings, secrets);

        Assert.Null(registry.GetConfigured("openrouter"));

        secrets.Set(SecretKeys.ApiKeyFor("openrouter"), "sk-test");
        Assert.NotNull(registry.GetConfigured("openrouter"));
    }

    [Fact]
    public void Registry_treats_local_openai_endpoints_as_configured_without_a_key()
    {
        var settings = new FakeSettingsStore();
        settings.Current.Providers.Add(Cloud("lmstudio", "http://localhost:1234/v1"));
        var registry = NewRegistry(settings, new FakeSecretStore());

        Assert.NotNull(registry.GetConfigured("lmstudio"));
    }

    // ── CapabilityRouter ──────────────────────────────────────────────────────

    [Fact]
    public void Router_resolves_explicit_chat_selection()
    {
        var settings = new FakeSettingsStore();
        settings.Current.Providers.Add(Cloud("openrouter", "https://openrouter.ai/api/v1", vision: true));
        var secrets = new FakeSecretStore();
        secrets.Set(SecretKeys.ApiKeyFor("openrouter"), "k");
        settings.Current.Capabilities.Chat = new ModelSelection("openrouter", "model-x");

        var router = new CapabilityRouter(settings, NewRegistry(settings, secrets));
        var resolved = router.Resolve(Capability.Chat);

        Assert.NotNull(resolved);
        Assert.Equal("openrouter", resolved!.Provider.Id);
        Assert.Equal("model-x", resolved.ModelId);
    }

    [Fact]
    public void Router_vision_picks_a_vision_capable_provider_and_writing_falls_back_to_chat()
    {
        var settings = new FakeSettingsStore();
        var ollama = new ProviderConnection
        {
            Id = "ollama", DisplayName = "Ollama", Kind = ProviderKind.Ollama,
            BaseUrl = "http://localhost:11434", DefaultModelId = "gemma",
        };
        settings.Current.Providers.Add(ollama);
        settings.Current.Providers.Add(Cloud("openrouter", "https://openrouter.ai/api/v1", vision: true, defaultModel: "vision-model"));

        var secrets = new FakeSecretStore();
        secrets.Set(SecretKeys.ApiKeyFor("openrouter"), "k");
        settings.Current.Capabilities.Chat = new ModelSelection("ollama", "gemma");

        var router = new CapabilityRouter(settings, NewRegistry(settings, secrets));

        // No explicit vision pick → first configured vision-capable provider (openrouter).
        var vision = router.Resolve(Capability.VisualIntelligence);
        Assert.NotNull(vision);
        Assert.Equal("openrouter", vision!.Provider.Id);

        // Writing has no override → falls back to the chat model (ollama).
        var writing = router.Resolve(Capability.WritingTools);
        Assert.NotNull(writing);
        Assert.Equal("ollama", writing!.Provider.Id);
    }

    // ── ProviderPresets ───────────────────────────────────────────────────────

    [Fact]
    public void Presets_have_unique_keys_and_a_custom_template()
    {
        var keys = ProviderPresets.All.Select(p => p.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ProviderPresets.All, p => Assert.False(string.IsNullOrWhiteSpace(p.DisplayName)));
        Assert.NotNull(ProviderPresets.ByKey(ProviderPresets.CustomKey));
    }

    [Fact]
    public void Preset_to_connection_carries_capabilities_and_default_model()
    {
        var connection = ProviderPresets.ByKey("openrouter")!.ToConnection("openrouter");

        Assert.Equal("openrouter", connection.Id);
        Assert.Equal(ProviderKind.OpenAiCompatible, connection.Kind);
        Assert.True(connection.SupportsVision);
        Assert.False(string.IsNullOrWhiteSpace(connection.DefaultModelId));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static ProviderConnection Cloud(string id, string baseUrl, bool vision = false, string? defaultModel = null) => new()
    {
        Id = id,
        DisplayName = id,
        Kind = ProviderKind.OpenAiCompatible,
        BaseUrl = baseUrl,
        SupportsVision = vision,
        DefaultModelId = defaultModel ?? "default-model",
    };

    private static ProviderRegistry NewRegistry(ISettingsStore settings, ISecretStore secrets) =>
        new(settings, secrets, new GoogleOAuthService(secrets));
}

file sealed class FakeSettingsStore : ISettingsStore
{
    public AppSettings Current { get; private set; } = new();
    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _store = new();
    public void Set(string key, string secret) => _store[key] = secret;
    public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
    public void Delete(string key) => _store.Remove(key);
}
