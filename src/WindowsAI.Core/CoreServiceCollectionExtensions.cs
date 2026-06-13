using Microsoft.Extensions.DependencyInjection;
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

        // Chat providers — resolved as a collection by the orchestrator.
        // Cloud providers depend on ISecretStore, which the host registers.
        services.AddSingleton<IChatProvider, OllamaChatProvider>();
        services.AddSingleton<IChatProvider, OpenRouterChatProvider>();
        services.AddSingleton<IChatProvider, GeminiChatProvider>();

        services.AddSingleton<IModelCatalog, ModelCatalog>();
        services.AddSingleton<ActiveModelService>();
        services.AddSingleton<ChatOrchestrator>();

        // Type-anywhere writing tools (proofread / rewrite / tone / summarize). UI-agnostic:
        // it transforms a text selection via the active model.
        services.AddSingleton<WritingToolsService>();

        // Text-to-image (OpenRouter image modality); depends on ISecretStore from the host.
        services.AddSingleton<ImageGenerationService>();

        // Local embeddings for semantic re-ranking of "Search my PC" results. The host
        // composes this with its platform retriever (see RerankingContextRetriever).
        services.AddSingleton<IEmbeddingClient, OllamaEmbeddingClient>();
        return services;
    }
}
