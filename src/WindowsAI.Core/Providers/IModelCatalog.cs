namespace WindowsAI.Core.Providers;

/// <summary>Discovers which models the user can pick: installed local models + curated cloud models.</summary>
public interface IModelCatalog
{
    Task<IReadOnlyList<ModelOption>> GetModelsAsync(CancellationToken ct = default);
}
