namespace WindowsAI.Core.Configuration;

/// <summary>
/// Root application settings, persisted as JSON. Secrets (API keys) are NOT stored
/// here — they live in <see cref="WindowsAI.Core.Security.ISecretStore"/>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>The model the user has selected for new conversations (provider + model id).</summary>
    public ModelSelection ActiveModel { get; set; } = new();

    public LocalModelSettings LocalModel { get; set; } = new();

    public OpenRouterSettings OpenRouter { get; set; } = new();

    public GeminiSettings Gemini { get; set; } = new();

    /// <summary>Theme + accent customization (defaults to following Windows).</summary>
    public AppearanceSettings Appearance { get; set; } = new();
}

/// <summary>Which light/dark theme the app uses.</summary>
public enum ThemeMode
{
    /// <summary>Follow the Windows light/dark setting (default).</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// Look-and-feel customization. By default everything follows Windows; the user can pin a
/// light/dark theme and/or override the accent with a custom color from Settings.
/// </summary>
public sealed class AppearanceSettings
{
    /// <summary>Light/dark mode. <see cref="ThemeMode.System"/> tracks the OS setting live.</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>
    /// Custom accent as <c>#RRGGBB</c>, or empty/null to follow the Windows accent color.
    /// </summary>
    public string? AccentColor { get; set; }
}

/// <summary>Identifies a concrete model: which provider serves it and the model id to request.</summary>
public sealed class ModelSelection
{
    public string ProviderId { get; set; } = "ollama";

    public string ModelId { get; set; } = "gemma4:e2b-it-qat";
}

/// <summary>Configuration for the local (Ollama) inference engine.</summary>
public sealed class LocalModelSettings
{
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Default local model tag. Gemma 4 2B / E2B (QAT, ~4.3 GB) — sized to fit on
    /// 8–16 GB integrated-graphics laptops.
    /// </summary>
    public string DefaultModelId { get; set; } = "gemma4:e2b-it-qat";

    /// <summary>
    /// Local embedding model used to re-rank "Search my PC" results by semantic relevance.
    /// Optional — if it isn't pulled, retrieval silently falls back to keyword ranking.
    /// </summary>
    public string EmbeddingModelId { get; set; } = "nomic-embed-text";

    /// <summary>How long Ollama keeps the model resident after a request (keeps the overlay snappy).</summary>
    public string KeepAlive { get; set; } = "10m";
}

/// <summary>Configuration for OpenRouter (OpenAI-compatible cloud, opt-in).</summary>
public sealed class OpenRouterSettings
{
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    public string DefaultModelId { get; set; } = "google/gemma-4-31b-it:free";

    /// <summary>Model used for text-to-image generation (OpenRouter, image modality).</summary>
    public string ImageModelId { get; set; } = "google/gemini-2.5-flash-image-preview";

    /// <summary>
    /// Vision model used by the screen-capture "ask about this region" flow. Must be a
    /// multimodal model on OpenRouter; defaults to a free one so the feature works out of the
    /// box once an OpenRouter key is added. User-configurable in Settings.
    /// </summary>
    public string VisionModelId { get; set; } = "meta-llama/llama-3.2-11b-vision-instruct:free";
}

/// <summary>Configuration for Google Gemini via its OpenAI-compatible endpoint (opt-in).</summary>
public sealed class GeminiSettings
{
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai";

    public string DefaultModelId { get; set; } = "gemini-2.5-flash";
}
