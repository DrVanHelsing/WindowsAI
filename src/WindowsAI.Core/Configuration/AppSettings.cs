namespace WindowsAI.Core.Configuration;

/// <summary>
/// Root application settings, persisted as JSON. Secrets (API keys, OAuth tokens) are NOT stored
/// here — they live in <see cref="WindowsAI.Core.Security.ISecretStore"/>.
///
/// <para>The schema is fully data-driven: instead of a fixed field per known backend, the user
/// configures an arbitrary list of <see cref="ProviderConnection"/>s through the UI, and each app
/// capability (chat, vision, writing, …) routes to a model on one of those connections. Older
/// (schema v1) files are upgraded in place by <c>SettingsMigrator</c>; the legacy fields below are
/// kept nullable purely so the migrator can read them.</para>
/// </summary>
public sealed class AppSettings
{
    /// <summary>The newest schema this build writes. Bump when the shape changes incompatibly.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Persisted schema version. Defaults to <c>0</c> so a pre-migration file (which has no such
    /// field) deserializes below <see cref="CurrentSchemaVersion"/> and triggers the migrator.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>User-configured chat backends. The single source of truth for "what can I talk to".</summary>
    public List<ProviderConnection> Providers { get; set; } = new();

    /// <summary>Which model each capability uses (chat, vision, writing, image, embeddings).</summary>
    public CapabilityRouting Capabilities { get; set; } = new();

    /// <summary>Per-feature enable flags and rebindable global hotkeys.</summary>
    public FeatureSettings Features { get; set; } = new();

    /// <summary>Theme + accent customization (defaults to following Windows / the brand accent).</summary>
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>"Search my PC" scope (folders surfaced to the Windows Search index).</summary>
    public LocalIndexSettings LocalIndex { get; set; } = new();

    /// <summary>First-run wizard state.</summary>
    public OnboardingState Onboarding { get; set; } = new();

    // ── Legacy (schema v1) — read once by SettingsMigrator, then dropped. Nullable so a
    //    migrated/fresh file omits them entirely. ──────────────────────────────────────────────

    /// <summary>Legacy v1 active model (provider id + model id). Migrated to <see cref="CapabilityRouting.Chat"/>.</summary>
    public ModelSelection? ActiveModel { get; set; }

    public LocalModelSettings? LocalModel { get; set; }
    public OpenRouterSettings? OpenRouter { get; set; }
    public GeminiSettings? Gemini { get; set; }
    public GoogleCloudSettings? GoogleCloud { get; set; }
    public WebSearchSettings? WebSearch { get; set; }
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
//  Providers (data-driven)
// ════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>How a <see cref="ProviderConnection"/> is reached at runtime.</summary>
public enum ProviderKind
{
    /// <summary>Any OpenAI chat-completions-compatible endpoint (OpenAI, OpenRouter, Gemini-compat, Groq, LM Studio, …).</summary>
    OpenAiCompatible,

    /// <summary>A local Ollama server (no key; live model list via <c>/api/tags</c>).</summary>
    Ollama,

    /// <summary>Gemini through Vertex AI's OpenAI-compatible endpoint, authenticated with a Google sign-in (OAuth).</summary>
    GoogleVertex,
}

/// <summary>
/// A single user-configured chat backend. The key/secret is never stored here — it lives in the
/// secret store under <see cref="WindowsAI.Core.Security.SecretKeys.ApiKeyFor"/>(<see cref="Id"/>).
/// </summary>
public sealed class ProviderConnection
{
    /// <summary>Stable identifier (slug for built-ins like "openrouter"; "custom-xxxxxxxx" for user-added).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-friendly name shown in the UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public ProviderKind Kind { get; set; } = ProviderKind.OpenAiCompatible;

    /// <summary>Base URL of the endpoint (OpenAI-compatible / Ollama). Unused for Vertex (computed from project+region).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>When false the connection is hidden from pickers and never used.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True if this backend's models can accept image input (multimodal).</summary>
    public bool SupportsVision { get; set; }

    /// <summary>True if this backend can generate images (OpenRouter-style image modality).</summary>
    public bool SupportsImageGeneration { get; set; }

    /// <summary>Curated/discovered model ids offered in pickers (the live Ollama list is merged in at runtime).</summary>
    public List<ProviderModel> Models { get; set; } = new();

    /// <summary>Default model id for this connection (used when a capability doesn't pin one).</summary>
    public string? DefaultModelId { get; set; }

    // ── Vertex (GoogleVertex) only ──
    public string? ProjectId { get; set; }
    public string? Location { get; set; }
    public string? OAuthClientId { get; set; }

    // ── Ollama only ──
    public string? KeepAlive { get; set; }
}

/// <summary>A selectable model on a provider connection.</summary>
public sealed record ProviderModel(string Id, string? DisplayName = null);

/// <summary>Identifies a concrete model: which provider connection serves it and the model id to request.</summary>
public sealed class ModelSelection
{
    public string ProviderId { get; set; } = "ollama";

    public string ModelId { get; set; } = "gemma4:e2b-it-qat";

    public ModelSelection() { }

    public ModelSelection(string providerId, string modelId)
    {
        ProviderId = providerId;
        ModelId = modelId;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(ProviderId) || string.IsNullOrWhiteSpace(ModelId);
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
//  Capability routing (per-feature model)
// ════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Which model each capability uses. <c>Chat</c> is the everyday model; the rest are optional
/// overrides — when null the <c>CapabilityRouter</c> falls back (writing → chat; vision/image →
/// first capable provider; embeddings → local/off).
/// </summary>
public sealed class CapabilityRouting
{
    public ModelSelection Chat { get; set; } = new();

    public ModelSelection? VisualIntelligence { get; set; }

    public ModelSelection? WritingTools { get; set; }

    public ModelSelection? ImageGeneration { get; set; }

    public ModelSelection? Embeddings { get; set; }
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
//  Feature flags + hotkeys
// ════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>Per-feature enable flags and the rebindable global hotkeys. Everything is user-configurable.</summary>
public sealed class FeatureSettings
{
    /// <summary>Screen-region capture + ask (Alt+Shift+S). Gates the hotkey and the vision flow.</summary>
    public bool VisualIntelligence { get; set; } = true;

    /// <summary>Type-anywhere writing tools (Ctrl+Shift+Space). Gates the hotkey and the flyout.</summary>
    public bool WritingTools { get; set; } = true;

    /// <summary>Tavily-backed <c>search_web</c> tool offered to the agent during chat.</summary>
    public bool WebSearch { get; set; } = true;

    /// <summary>Text-to-image generation in chat.</summary>
    public bool ImageGeneration { get; set; } = true;

    /// <summary>"Search my PC" (<c>search_my_pc</c> / <c>open_path</c>) backed by the Windows Search index.</summary>
    public bool PcIndexing { get; set; } = true;

    /// <summary>Global hotkey that summons the overlay search surface.</summary>
    public HotkeyChord SummonHotkey { get; set; } = HotkeyChord.AltSpace;

    /// <summary>Global hotkey that opens the writing tools over the current selection.</summary>
    public HotkeyChord WritingToolsHotkey { get; set; } = HotkeyChord.CtrlShiftSpace;

    /// <summary>Global hotkey that starts a visual-intelligence region capture.</summary>
    public HotkeyChord VisualIntelligenceHotkey { get; set; } = HotkeyChord.AltShiftS;
}

/// <summary>
/// A serializable global-hotkey chord (Win32 RegisterHotKey modifier flags + virtual-key code).
/// Lives in Core (platform-agnostic); the Services layer maps it to its native HotkeyDefinition.
/// </summary>
public sealed class HotkeyChord
{
    // Win32 RegisterHotKey modifier flags.
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    public uint Modifiers { get; set; }

    public uint VirtualKey { get; set; }

    public HotkeyChord() { }

    public HotkeyChord(uint modifiers, uint virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    public static HotkeyChord AltSpace => new(MOD_ALT, 0x20 /* VK_SPACE */);
    public static HotkeyChord CtrlShiftSpace => new(MOD_CONTROL | MOD_SHIFT, 0x20 /* VK_SPACE */);
    public static HotkeyChord AltShiftS => new(MOD_ALT | MOD_SHIFT, 0x53 /* VK_S */);
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
//  Local index ("Search my PC")
// ════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Folders the user has added to the Windows Search index scope for "Search my PC". The app does
/// not build its own index — it manages the OS index scope and reads its status.
/// </summary>
public sealed class LocalIndexSettings
{
    /// <summary>Folders the user explicitly added through the app (for display/management).</summary>
    public List<string> Folders { get; set; } = new();
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
//  Onboarding
// ════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>First-run wizard state, so onboarding shows exactly once (until reset from Settings).</summary>
public sealed class OnboardingState
{
    /// <summary>True once the user has completed or skipped the wizard.</summary>
    public bool HasCompleted { get; set; }

    /// <summary>App version that completed onboarding (lets a future build re-show "what's new").</summary>
    public string? Version { get; set; }
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
//  Appearance (current)
// ════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>Which light/dark theme the app uses.</summary>
public enum ThemeMode
{
    /// <summary>Follow the Windows light/dark setting (default).</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// Look-and-feel customization. By default the theme follows Windows; the accent defaults to the
/// brand "Electric Iris" (the user can switch to "Windows default" (null) or any custom color).
/// </summary>
public sealed class AppearanceSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>Custom accent as <c>#RRGGBB</c>, or null to follow the Windows accent color.</summary>
    public string? AccentColor { get; set; } = "#6E7BFF";
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
//  Web search tool (current)
// ════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Configuration for the <c>search_web</c> agent tool, backed by Tavily's Search API. The API key
/// is a secret (<see cref="WindowsAI.Core.Security.SecretKeys.TavilyApiKey"/>). The enable flag now
/// lives on <see cref="FeatureSettings.WebSearch"/>; this legacy type is read by the migrator.
/// </summary>
public sealed class WebSearchSettings
{
    public bool Enabled { get; set; } = true;
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
//  Legacy v1 provider settings — retained for migration only. New code uses ProviderConnection.
// ════════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>Legacy v1 local (Ollama) settings. Migrated into an Ollama <see cref="ProviderConnection"/>.</summary>
public sealed class LocalModelSettings
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string DefaultModelId { get; set; } = "gemma4:e2b-it-qat";
    public string EmbeddingModelId { get; set; } = "nomic-embed-text";
    public string KeepAlive { get; set; } = "10m";
}

/// <summary>Legacy v1 OpenRouter settings. Migrated into an OpenAI-compatible <see cref="ProviderConnection"/>.</summary>
public sealed class OpenRouterSettings
{
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string DefaultModelId { get; set; } = "google/gemma-4-31b-it:free";
    public string ImageModelId { get; set; } = "google/gemini-2.5-flash-image-preview";
    public string VisionModelId { get; set; } = "google/gemma-4-31b-it:free";
}

/// <summary>Legacy v1 Gemini settings. Migrated into an OpenAI-compatible <see cref="ProviderConnection"/>.</summary>
public sealed class GeminiSettings
{
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai";
    public string DefaultModelId { get; set; } = "gemini-3.5-flash";
}

/// <summary>Legacy v1 Google Cloud / Vertex settings. Migrated into a GoogleVertex <see cref="ProviderConnection"/>.</summary>
public sealed class GoogleCloudSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Location { get; set; } = "global";
    public string DefaultModelId { get; set; } = "google/gemini-3.5-flash";
}
