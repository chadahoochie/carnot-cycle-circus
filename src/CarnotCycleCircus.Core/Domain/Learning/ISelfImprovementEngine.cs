namespace CarnotCycleCircus.Core.Domain.Learning;

public record SelfImprovementReport(
    int TotalCyclesRun,
    int InsightsDistilledCount,
    int ProceduralRecipesGenerated,
    int SemanticRulesReinforced,
    int MemoriesConsolidatedCount,
    int DecayedMemoriesPrunedCount,
    IReadOnlyList<string> DistilledInsights,
    DateTimeOffset Timestamp
);

public interface ISelfImprovementEngine
{
    SelfImprovementReport GetLatestReport();
    Task<SelfImprovementReport> RunSelfImprovementCycleAsync(CancellationToken cancellationToken = default);
    event Action<SelfImprovementReport>? OnSelfImprovementCompleted;
}
