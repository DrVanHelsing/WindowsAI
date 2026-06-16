using WindowsAI.Core.Configuration;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Builds the model picker list from the user's configured provider connections: locally installed
/// Ollama models (queried live) plus each connection's curated model list. Fully data-driven — no
/// hardcoded providers or model ids; the curated lists are populated by "Fetch models" in the UI.
/// </summary>
public sealed class ModelCatalog : IModelCatalog
{
    private readonly ISettingsStore _settings;
    private readonly ProviderProbe _probe;

    public ModelCatalog(ISettingsStore settings, ProviderProbe probe)
    {
        _settings = settings;
        _probe = probe;
    }

    public async Task<IReadOnlyList<ModelOption>> GetModelsAsync(CancellationToken ct = default)
    {
        var models = new List<ModelOption>();

        foreach (var connection in _settings.Current.Providers)
        {
            if (!connection.Enabled)
                continue;

            var isLocal = IsLocal(connection);

            if (connection.Kind == ProviderKind.Ollama)
            {
                // Live tags are the source of truth for a local Ollama server; fall back to the
                // curated list if it's unreachable so the default model still appears.
                IReadOnlyList<ProviderModel> live;
                try { live = await _probe.ListModelsAsync(connection, ct); }
                catch { live = connection.Models; }

                foreach (var m in live.Where(m => !IsEmbeddingModel(m.Id)))
                    models.Add(new ModelOption(connection.Id, m.Id, $"{m.Id} (local)", isLocal: true));
            }
            else
            {
                foreach (var m in connection.Models)
                    models.Add(new ModelOption(
                        connection.Id, m.Id, $"{m.DisplayName ?? m.Id} ({connection.DisplayName})", isLocal));
            }
        }

        return models;
    }

    /// <summary>Local = an Ollama connection or a loopback OpenAI-compatible server (LM Studio, llama.cpp).</summary>
    private static bool IsLocal(ProviderConnection c) =>
        c.Kind == ProviderKind.Ollama
        || (Uri.TryCreate(c.BaseUrl, UriKind.Absolute, out var u)
            && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)));

    /// <summary>True for models that produce embeddings rather than chat completions.</summary>
    private static bool IsEmbeddingModel(string name) =>
        name.Contains("embed", StringComparison.OrdinalIgnoreCase);
}
