using Microsoft.Extensions.DependencyInjection;
using WindowsAI.Core.Chat;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.History;
using WindowsAI.Core.Providers;

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
        return services;
    }
}
