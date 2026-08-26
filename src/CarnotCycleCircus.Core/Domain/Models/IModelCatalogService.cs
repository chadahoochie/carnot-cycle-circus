using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Models;

public interface IModelCatalogService
{
    Task<IReadOnlyList<ModelCatalogEntry>> GetModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModelCatalogEntry>> GetFavoritesAsync(CancellationToken cancellationToken = default);
    Task<bool> ToggleFavoriteAsync(string modelId, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(string modelId, bool isFavorite, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModelCatalogEntry>> GetRecommendedModelsAsync(AgentRole role, CancellationToken cancellationToken = default);
    Task<ModelCatalogHealthStatus> GetCatalogStatusAsync(CancellationToken cancellationToken = default);
    Task<ModelCatalogEntry?> GetModelByIdAsync(string modelId, CancellationToken cancellationToken = default);
}
