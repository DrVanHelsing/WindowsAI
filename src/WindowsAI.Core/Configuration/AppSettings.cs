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

    /// <summary>Google Cloud / Vertex AI access via Google sign-in (OAuth), as an alternative to a Gemini API key.</summary>
    public GoogleCloudSettings GoogleCloud { get; set; } = new();

    /// <summary>Theme + accent customization (defaults to following Windows).</summary>
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>Web-search tool (Tavily) the agent can call during chat.</summary>
    public WebSearchSettings WebSearch { get; set; } = new();
}

/// <summary>
/// Configuration for the <c>search_web</c> agent tool, backed by Tavily's Search API. The API key
/// is a secret (<see cref="WindowsAI.Core.Security.SecretKeys.TavilyApiKey"/>); nothing else is
/// required. The user creates a key (free tier available) at tavily.com.
/// </summary>
public sealed class WebSearchSettings
{
    /// <summary>When true (and a key is present), the agent is offered the <c>search_web</c> tool.</summary>
    public bool Enabled { get; set; } = true;
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
    /// Defaults to the brand "Electric Iris" so the app ships with its own identity; the user
    /// can switch to "Windows default" (null) or any preset from Settings.
    /// </summary>
    public string? AccentColor { get; set; } = "#6E7BFF";
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
    /// Vision model used by the screen-capture "ask about this region" flow. Must be a multimodal
    /// (image-input) model on OpenRouter; defaults to a free one so the feature works out of the
    /// box once an OpenRouter key is added. Gemma 4 31B is a strong free all-rounder for screenshots
    /// (text/UI/documents and general imagery); swap to nvidia/nemotron-nano-12b-v2-vl:free for
    /// faster OCR/document-heavy use. User-configurable in Settings.
    /// </summary>
    public string VisionModelId { get; set; } = "google/gemma-4-31b-it:free";
}

/// <summary>Configuration for Google Gemini via its OpenAI-compatible endpoint (opt-in).</summary>
public sealed class GeminiSettings
{
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai";

    public string DefaultModelId { get; set; } = "gemini-3.5-flash";
}

/// <summary>
/// Configuration for reaching Gemini through Vertex AI's OpenAI-compatible endpoint using a
/// Google sign-in (OAuth) instead of an API key. The OAuth client id lives here (it isn't a
/// secret for an installed/desktop app); the client secret and the refresh token are held in
/// <see cref="WindowsAI.Core.Security.ISecretStore"/>. The user creates the OAuth "Desktop app"
/// client in their own Google Cloud project and pastes the id + project here.
/// </summary>
public sealed class GoogleCloudSettings
{
    /// <summary>OAuth 2.0 client id (type "Desktop app") from the user's Google Cloud project.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The Google Cloud project id that has the Vertex AI API enabled.</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Vertex AI region serving the model. Defaults to <c>global</c> because the current Gemini
    /// generation (3.x) is only served there — regional hosts like <c>us-central1</c> 404 on them.
    /// </summary>
    public string Location { get; set; } = "global";

    /// <summary>
    /// Default model id for the Vertex OpenAI-compatible endpoint. Vertex expects the
    /// publisher-qualified form, e.g. <c>google/gemini-3.5-flash</c> (the latest GA flash; verified
    /// live via Model Garden 2026-06-15).
    /// </summary>
    public string DefaultModelId { get; set; } = "google/gemini-3.5-flash";
}
