namespace CarnotCycleCircus.Core.Domain.Storage;

public record CarnotStorageOptions
{
    public string DataDirectory { get; init; } = Environment.GetEnvironmentVariable("CARNOT_DATA_DIR") ?? Path.Combine(AppContext.BaseDirectory, "data");
    public bool EnableAtomicWrites { get; init; } = true;
    public int SelfImprovementIntervalSeconds { get; init; } = int.TryParse(Environment.GetEnvironmentVariable("CARNOT_SELF_IMPROVEMENT_INTERVAL_SECONDS"), out var sec) ? Math.Max(10, sec) : 300;
    public bool AutoRunSelfImprovementOnStartup { get; init; } = true;

    public string ArtifactsDirectory => Path.Combine(DataDirectory, "artifacts");
    public string SkillsDirectory => Path.Combine(DataDirectory, "skills");
    public string AdrsDirectory => Path.Combine(ArtifactsDirectory, "adrs");
}
