namespace WindowsAI.Core.Providers;

/// <summary>
/// Builds and caches the runtime <see cref="IChatProvider"/>s from the user-configured
/// <c>AppSettings.Providers</c> list. Replaces the old fixed DI collection: providers are now
/// data-driven, so adding/editing/removing a connection just calls <see cref="Invalidate"/>.
/// Only enabled connections are surfaced.
/// </summary>
public interface IProviderRegistry
{
    /// <summary>All enabled providers, in configured order.</summary>
    IReadOnlyList<IChatProvider> All { get; }

    /// <summary>The enabled provider with this id, or null.</summary>
    IChatProvider? GetById(string id);

    /// <summary>The provider with this id only if it is configured (ready to use), else null.</summary>
    IChatProvider? GetConfigured(string id);

    /// <summary>Raised after the provider set is rebuilt (so dependent UI can refresh).</summary>
    event EventHandler? Changed;

    /// <summary>Rebuilds the provider set from current settings and raises <see cref="Changed"/>.</summary>
    void Invalidate();
}
