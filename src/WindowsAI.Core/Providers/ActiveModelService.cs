using WindowsAI.Core.Configuration;

namespace WindowsAI.Core.Providers;

/// <summary>
/// The single source of truth for which model is active. Persists the selection through
/// <see cref="ISettingsStore"/> and raises <see cref="Changed"/> so every surface (the shell
/// chat, the models page, and the overlay) can update live when the choice changes anywhere.
/// </summary>
public sealed class ActiveModelService
{
    private readonly ISettingsStore _settings;

    /// <summary>Raised after the active model changes. Handlers run on the caller's thread.</summary>
    public event EventHandler? Changed;

    public ActiveModelService(ISettingsStore settings) => _settings = settings;

    /// <summary>The currently selected provider + model id (the Chat capability).</summary>
    public ModelSelection Current => _settings.Current.Capabilities.Chat;

    /// <summary>Sets and persists the active model, notifying listeners. No-op if unchanged.</summary>
    public async Task SetAsync(string providerId, string modelId, CancellationToken ct = default)
    {
        if (Current.ProviderId == providerId && Current.ModelId == modelId)
            return;

        Current.ProviderId = providerId;
        Current.ModelId = modelId;
        await _settings.SaveAsync(ct);

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
