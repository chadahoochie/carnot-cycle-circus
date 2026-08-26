namespace CarnotCycleCircus.Core.Domain.Storage;

public record CarnotStorageOptions
{
    private readonly string? _dataDirectory;
    private readonly string? _artifactsDirectory;

    public string DataDirectory
    {
        get => _dataDirectory ?? ResolveDefaultDataDirectory();
        init => _dataDirectory = value;
    }

    public string ArtifactsDirectory
    {
        get
        {
            if (_artifactsDirectory != null) return _artifactsDirectory;
            if (_dataDirectory != null) return Path.Combine(_dataDirectory, "artifacts");
            return ResolveDefaultArtifactsDirectory();
        }
        init => _artifactsDirectory = value;
    }

    public bool EnableAtomicWrites { get; init; } = true;
    public int SelfImprovementIntervalSeconds { get; init; } = int.TryParse(Environment.GetEnvironmentVariable("CARNOT_SELF_IMPROVEMENT_INTERVAL_SECONDS"), out var sec) ? Math.Max(10, sec) : 300;
    public bool AutoRunSelfImprovementOnStartup { get; init; } = true;

    public string SkillsDirectory => Path.Combine(DataDirectory, "skills");
    public string AdrsDirectory => Path.Combine(ArtifactsDirectory, "adrs");

    public static string ResolveDefaultDataDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("CARNOT_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            return envDir;
        }

        var repoRoot = FindRepoRoot();
        if (repoRoot != null)
        {
            return Path.Combine(repoRoot, "data");
        }

        return Path.Combine(AppContext.BaseDirectory, "data");
    }

    public static string ResolveDefaultArtifactsDirectory()
    {
        var envArtifacts = Environment.GetEnvironmentVariable("CARNOT_ARTIFACTS_DIR");
        if (!string.IsNullOrWhiteSpace(envArtifacts))
        {
            return envArtifacts;
        }

        var envDir = Environment.GetEnvironmentVariable("CARNOT_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            return Path.Combine(envDir, "artifacts");
        }

        var repoRoot = FindRepoRoot();
        if (repoRoot != null)
        {
            return Path.Combine(repoRoot, "artifacts");
        }

        return Path.Combine(AppContext.BaseDirectory, "data", "artifacts");
    }

    public static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CarnotCycleCircus.slnx")) ||
                File.Exists(Path.Combine(current.FullName, "CarnotCycleCircus.sln")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null)
        {
            if (File.Exists(Path.Combine(currentDir.FullName, "CarnotCycleCircus.slnx")) ||
                File.Exists(Path.Combine(currentDir.FullName, "CarnotCycleCircus.sln")) ||
                Directory.Exists(Path.Combine(currentDir.FullName, ".git")))
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }

        return null;
    }
}
