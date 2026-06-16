using WindowsAI.Core.Configuration;

namespace WindowsAI.Core.Providers;

/// <summary>
/// A starting template for a provider connection — pure data used to pre-fill the "Add provider"
/// form and the onboarding gallery. Nothing here is hardcoded behavior: the user edits any field
/// after picking, and "Custom" starts from a blank OpenAI-compatible connection.
/// </summary>
public sealed record ProviderPreset(
    string Key,
    string DisplayName,
    ProviderKind Kind,
    string BaseUrl,
    string KeyPlaceholder,
    string? GetKeyUrl,
    bool SupportsVision,
    bool SupportsImageGeneration,
    IReadOnlyList<string> SuggestedModels,
    string? Blurb = null)
{
    /// <summary>Builds a connection from this preset with the given (already-unique) id.</summary>
    public ProviderConnection ToConnection(string id) => new()
    {
        Id = id,
        DisplayName = DisplayName,
        Kind = Kind,
        BaseUrl = BaseUrl,
        Enabled = true,
        SupportsVision = SupportsVision,
        SupportsImageGeneration = SupportsImageGeneration,
        Models = SuggestedModels.Select(m => new ProviderModel(m)).ToList(),
        DefaultModelId = SuggestedModels.FirstOrDefault(),
        Location = Kind == ProviderKind.GoogleVertex ? "global" : null,
        KeepAlive = Kind == ProviderKind.Ollama ? "10m" : null,
    };
}

/// <summary>The built-in catalog of provider presets, ordered for the gallery.</summary>
public static class ProviderPresets
{
    /// <summary>Stable preset key for a blank custom OpenAI-compatible connection.</summary>
    public const string CustomKey = "custom";

    public static IReadOnlyList<ProviderPreset> All { get; } = new ProviderPreset[]
    {
        new("openrouter", "OpenRouter", ProviderKind.OpenAiCompatible,
            "https://openrouter.ai/api/v1", "sk-or-…", "https://openrouter.ai/keys",
            SupportsVision: true, SupportsImageGeneration: true,
            ["google/gemma-4-31b-it:free", "deepseek/deepseek-r1:free", "openai/gpt-5", "anthropic/claude-opus-4.8"],
            "One key, hundreds of models (including free ones)."),

        new("openai", "OpenAI", ProviderKind.OpenAiCompatible,
            "https://api.openai.com/v1", "sk-…", "https://platform.openai.com/api-keys",
            SupportsVision: true, SupportsImageGeneration: false,
            ["gpt-5", "gpt-5-mini", "o4"],
            "GPT models direct from OpenAI."),

        new("gemini", "Google Gemini", ProviderKind.OpenAiCompatible,
            "https://generativelanguage.googleapis.com/v1beta/openai", "AIza…", "https://aistudio.google.com/apikey",
            SupportsVision: true, SupportsImageGeneration: false,
            ["gemini-3.5-flash", "gemini-3.1-pro-preview", "gemini-2.5-flash"],
            "Gemini via an API key from Google AI Studio."),

        new("anthropic", "Anthropic (Claude)", ProviderKind.OpenAiCompatible,
            "https://api.anthropic.com/v1", "sk-ant-…", "https://console.anthropic.com/settings/keys",
            SupportsVision: true, SupportsImageGeneration: false,
            ["claude-opus-4-8", "claude-sonnet-4-6", "claude-haiku-4-5"],
            "Claude models via Anthropic's OpenAI-compatible endpoint."),

        new("groq", "Groq", ProviderKind.OpenAiCompatible,
            "https://api.groq.com/openai/v1", "gsk_…", "https://console.groq.com/keys",
            SupportsVision: true, SupportsImageGeneration: false,
            ["llama-3.3-70b-versatile", "openai/gpt-oss-120b"],
            "Very fast open models on Groq's LPU."),

        new("deepseek", "DeepSeek", ProviderKind.OpenAiCompatible,
            "https://api.deepseek.com/v1", "sk-…", "https://platform.deepseek.com/api_keys",
            SupportsVision: false, SupportsImageGeneration: false,
            ["deepseek-chat", "deepseek-reasoner"],
            "DeepSeek chat + reasoning models."),

        new("mistral", "Mistral", ProviderKind.OpenAiCompatible,
            "https://api.mistral.ai/v1", "…", "https://console.mistral.ai/api-keys",
            SupportsVision: true, SupportsImageGeneration: false,
            ["mistral-large-latest", "pixtral-large-latest"],
            "Mistral's open and frontier models."),

        new("together", "Together AI", ProviderKind.OpenAiCompatible,
            "https://api.together.xyz/v1", "…", "https://api.together.ai/settings/api-keys",
            SupportsVision: true, SupportsImageGeneration: true,
            ["meta-llama/Llama-4-Scout-17B-16E-Instruct"],
            "A broad catalog of open models."),

        new("xai", "xAI (Grok)", ProviderKind.OpenAiCompatible,
            "https://api.x.ai/v1", "xai-…", "https://console.x.ai",
            SupportsVision: true, SupportsImageGeneration: false,
            ["grok-4", "grok-4-fast"],
            "Grok models from xAI."),

        new("azure-openai", "Azure OpenAI", ProviderKind.OpenAiCompatible,
            "https://YOUR-RESOURCE.openai.azure.com/openai/v1", "…", "https://portal.azure.com",
            SupportsVision: true, SupportsImageGeneration: false,
            [],
            "Your Azure OpenAI deployment (edit the base URL)."),

        new("ollama", "Ollama (local)", ProviderKind.Ollama,
            "http://localhost:11434", "", "https://ollama.com/download",
            SupportsVision: false, SupportsImageGeneration: false,
            ["gemma4:e2b-it-qat"],
            "Run models fully on this PC. Optional — no key needed."),

        new("lmstudio", "LM Studio (local)", ProviderKind.OpenAiCompatible,
            "http://localhost:1234/v1", "", "https://lmstudio.ai",
            SupportsVision: true, SupportsImageGeneration: false,
            [],
            "Local OpenAI-compatible server from LM Studio. No key needed."),

        new("vertex", "Gemini via Google sign-in", ProviderKind.GoogleVertex,
            "", "", "https://console.cloud.google.com/apis/credentials",
            SupportsVision: true, SupportsImageGeneration: false,
            ["google/gemini-3.5-flash", "google/gemini-3.1-pro-preview"],
            "Gemini through Vertex AI using a Google login instead of a key."),

        new(CustomKey, "Custom (OpenAI-compatible)", ProviderKind.OpenAiCompatible,
            "", "", null,
            SupportsVision: false, SupportsImageGeneration: false,
            [],
            "Point at any endpoint that speaks the OpenAI chat-completions API."),
    };

    public static ProviderPreset? ByKey(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
