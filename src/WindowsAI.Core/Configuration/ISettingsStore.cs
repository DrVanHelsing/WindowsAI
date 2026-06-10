namespace WindowsAI.Core.Configuration;

/// <summary>Loads and persists <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    /// <summary>The current in-memory settings. Never null after construction.</summary>
    AppSettings Current { get; }

    Task LoadAsync(CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);
}
