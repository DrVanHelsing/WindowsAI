using Microsoft.Extensions.DependencyInjection;
using WindowsAI.Core.Auth;
using WindowsAI.Core.Chat;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Context;
using WindowsAI.Core.History;
using WindowsAI.Core.Providers;
using WindowsAI.Core.Writing;

namespace WindowsAI.Core;

/// <summary>DI registration for the platform-agnostic core (chat, providers, settings).</summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsAiCore(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IConversationStore, JsonConversationStore>();

        // Data-driven providers: the registry builds one IChatProvider per user-configured
        // connection (OpenAI-compatible / Ollama / Vertex). GoogleOAuthService backs Vertex
        // sign-in; both depend on ISecretStore, which the host registers.
        services.AddSingleton<GoogleOAuthService>();
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddSingleton<ProviderProbe>();
        services.AddSingleton<CapabilityRouter>();

        services.AddSingleton<IModelCatalog, ModelCatalog>();
        services.AddSingleton<ActiveModelService>();
        services.AddSingleton<ChatOrchestrator>();

        // Type-anywhere writing tools (proofread / rewrite / tone / summarize). UI-agnostic:
        // it transforms a text selection via the active model.
        services.AddSingleton<WritingToolsService>();

        // Text-to-image (OpenRouter image modality); depends on ISecretStore from the host.
        services.AddSingleton<ImageGenerationService>();

        // Visual intelligence: answers questions about a captured screen region via a
        // configurable OpenRouter vision model (always cloud, regardless of the active model).
        services.AddSingleton<VisionService>();

        // Web search (Tavily) backing the agent's search_web tool.
        services.AddSingleton<Tools.WebSearchService>();

        // Local embeddings for semantic re-ranking of "Search my PC" results. The host
        // composes this with its platform retriever (see RerankingContextRetriever).
        services.AddSingleton<IEmbeddingClient, OllamaEmbeddingClient>();
        return services;
    }
}
