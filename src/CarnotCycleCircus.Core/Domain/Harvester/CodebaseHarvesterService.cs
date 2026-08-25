using System.Text.RegularExpressions;
using System.Xml.Linq;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Docs;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Knowledge;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tickets;

namespace CarnotCycleCircus.Core.Domain.Harvester;

public record DiscoveredProjectInfo(
    string Name,
    string RelativePath,
    string TargetFramework,
    IReadOnlyList<string> PackageReferences,
    string ProjectType
);

public record CodebaseHarvestReport(
    string RootPath,
    string SolutionName,
    int TotalFiles,
    int CSharpFilesCount,
    int TestFilesCount,
    int DocumentationFilesCount,
    IReadOnlyList<DiscoveredProjectInfo> Projects,
    IReadOnlyList<string> DetectedPatterns,
    IReadOnlyList<string> QualityInsights,
    IReadOnlyList<string> GeneratedTicketIds,
    DateTimeOffset ScannedAt
);

public interface ICodebaseHarvesterService
{
    Task<CodebaseHarvestReport> HarvestDirectoryAsync(string directoryPath, bool autoGenerateBacklog = true, CancellationToken cancellationToken = default);
    Task<CodebaseHarvestReport> HarvestCurrentWorkspaceAsync(bool autoGenerateBacklog = true, CancellationToken cancellationToken = default);
    CodebaseHarvestReport? GetLatestReport();
}

public class CodebaseHarvesterService : ICodebaseHarvesterService
{
    private readonly IKnowledgeMapService _knowledgeMap;
    private readonly IPersistentMemoryStore _memoryStore;
    private readonly ITicketStore _ticketStore;
    private readonly IWorkDecompositionEngine _decompositionEngine;
    private readonly IAdrDocumentManager _adrManager;
    private readonly IAgentEventStream _eventStream;
    private CodebaseHarvestReport? _latestReport;

    public CodebaseHarvesterService(
        IKnowledgeMapService knowledgeMap,
        IPersistentMemoryStore memoryStore,
        ITicketStore ticketStore,
        IWorkDecompositionEngine decompositionEngine,
        IAdrDocumentManager adrManager,
        IAgentEventStream eventStream)
    {
        _knowledgeMap = knowledgeMap;
        _memoryStore = memoryStore;
        _ticketStore = ticketStore;
        _decompositionEngine = decompositionEngine;
        _adrManager = adrManager;
        _eventStream = eventStream;
    }

    public CodebaseHarvestReport? GetLatestReport() => _latestReport;

    public Task<CodebaseHarvestReport> HarvestCurrentWorkspaceAsync(bool autoGenerateBacklog = true, CancellationToken cancellationToken = default)
    {
        var currentDir = Directory.GetCurrentDirectory();
        return HarvestDirectoryAsync(currentDir, autoGenerateBacklog, cancellationToken);
    }

    public async Task<CodebaseHarvestReport> HarvestDirectoryAsync(
        string directoryPath,
        bool autoGenerateBacklog = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            directoryPath = Directory.GetCurrentDirectory();
        }

        var dirInfo = new DirectoryInfo(directoryPath);

        // If directory is inside bin/obj or child folders, traverse upwards to find root solution/repo
        var candidate = dirInfo;
        while (candidate != null && candidate.Parent != null)
        {
            if (Directory.EnumerateFiles(candidate.FullName, "*.sln*", SearchOption.TopDirectoryOnly).Any() ||
                Directory.Exists(Path.Combine(candidate.FullName, ".git")) ||
                Directory.Exists(Path.Combine(candidate.FullName, "src")))
            {
                dirInfo = candidate;
                directoryPath = dirInfo.FullName;
                break;
            }
            candidate = candidate.Parent;
        }

        var slnFile = Directory.EnumerateFiles(directoryPath, "*.sln*", SearchOption.TopDirectoryOnly).FirstOrDefault()
                      ?? Directory.EnumerateFiles(directoryPath, "*.sln*", SearchOption.AllDirectories).FirstOrDefault();
        var solutionName = slnFile != null ? Path.GetFileNameWithoutExtension(slnFile) : dirInfo.Name;

        var allFiles = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var rel = Path.GetRelativePath(directoryPath, f).Replace('\\', '/');
                return !rel.StartsWith("bin/") && !rel.Contains("/bin/") &&
                       !rel.StartsWith("obj/") && !rel.Contains("/obj/") &&
                       !rel.StartsWith(".git/") && !rel.Contains("/.git/") &&
                       !rel.StartsWith("node_modules/") && !rel.Contains("/node_modules/");
            })
            .ToList();

        var csFiles = allFiles.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList();
        var testFiles = csFiles.Where(f => f.Contains("test", StringComparison.OrdinalIgnoreCase) || f.Contains("spec", StringComparison.OrdinalIgnoreCase)).ToList();
        var docFiles = allFiles.Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();

        // Discover .csproj / project files
        var projectFiles = Directory.EnumerateFiles(directoryPath, "*.csproj", SearchOption.AllDirectories)
            .Where(f =>
            {
                var rel = Path.GetRelativePath(directoryPath, f).Replace('\\', '/');
                return !rel.StartsWith("bin/") && !rel.Contains("/bin/") &&
                       !rel.StartsWith("obj/") && !rel.Contains("/obj/");
            })
            .ToList();

        var discoveredProjects = new List<DiscoveredProjectInfo>();
        var allPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projPath in projectFiles)
        {
            var projName = Path.GetFileNameWithoutExtension(projPath);
            var relPath = Path.GetRelativePath(directoryPath, projPath);
            var packages = new List<string>();
            var targetFramework = "Unknown";

            try
            {
                var content = await File.ReadAllTextAsync(projPath, cancellationToken);
                var tfMatch = Regex.Match(content, @"<TargetFramework>(.*?)</TargetFramework>");
                if (tfMatch.Success) targetFramework = tfMatch.Groups[1].Value;

                var pkgMatches = Regex.Matches(content, @"<PackageReference\s+Include=""([^""]+)""");
                foreach (Match m in pkgMatches)
                {
                    if (m.Success)
                    {
                        var pkg = m.Groups[1].Value;
                        packages.Add(pkg);
                        allPackages.Add(pkg);
                    }
                }
            }
            catch
            {
                // Fallback for unreadable xml
            }

            var pType = projName.Contains("Test", StringComparison.OrdinalIgnoreCase) ? "Test Suite" :
                        projName.Contains("Web", StringComparison.OrdinalIgnoreCase) || projName.Contains("Api", StringComparison.OrdinalIgnoreCase) ? "Web Service / App" :
                        projName.Contains("Core", StringComparison.OrdinalIgnoreCase) || projName.Contains("Domain", StringComparison.OrdinalIgnoreCase) ? "Core Domain" : "Class Library";

            discoveredProjects.Add(new DiscoveredProjectInfo(projName, relPath, targetFramework, packages, pType));
        }

        // Detect Architectural Patterns
        var detectedPatterns = new List<string>();
        if (allPackages.Any(p => p.Contains("xunit", StringComparison.OrdinalIgnoreCase) || p.Contains("fluentassertions", StringComparison.OrdinalIgnoreCase)))
            detectedPatterns.Add("🧪 xUnit & FluentAssertions Automated Quality Harness");
        if (allPackages.Any(p => p.Contains("blazor", StringComparison.OrdinalIgnoreCase)) || allFiles.Any(f => f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
            detectedPatterns.Add("🌐 Interactive Blazor Web Architecture");
        if (allPackages.Any(p => p.Contains("opentelemetry", StringComparison.OrdinalIgnoreCase)))
            detectedPatterns.Add("📊 OpenTelemetry Distributed Tracing & Observability");
        if (allPackages.Any(p => p.Contains("redis", StringComparison.OrdinalIgnoreCase)))
            detectedPatterns.Add("⚡ Redis High-Speed In-Memory Data Tier");
        if (allPackages.Any(p => p.Contains("entityframework", StringComparison.OrdinalIgnoreCase) || p.Contains("efcore", StringComparison.OrdinalIgnoreCase)))
            detectedPatterns.Add("💾 Entity Framework Core Relational Data Layer");
        if (allPackages.Any(p => p.Contains("akka", StringComparison.OrdinalIgnoreCase)))
            detectedPatterns.Add("🎭 Akka.NET Clustered Actor Concurrency Engine");
        if (csFiles.Any(f => File.ReadAllText(f).Contains("ValueTask") || File.ReadAllText(f).Contains("ReadOnlyMemory")))
            detectedPatterns.Add("⚡ Zero-Allocation ValueTask / Span Memory Hotpaths");
        if (docFiles.Any(f => f.Contains("adr", StringComparison.OrdinalIgnoreCase) || f.Contains("ADR")))
            detectedPatterns.Add("🏛️ Architectural Decision Records (Docs-as-Code)");

        if (detectedPatterns.Count == 0)
        {
            detectedPatterns.Add("📦 Standard Modular .NET Architecture");
        }

        // Quality & Tech Debt Insights
        var qualityInsights = new List<string>();
        var testRatio = csFiles.Count > 0 ? (double)testFiles.Count / csFiles.Count : 0.0;
        qualityInsights.Add($"📁 Scanned {csFiles.Count} C# files across {discoveredProjects.Count} project modules.");

        if (testFiles.Count == 0 || testRatio < 0.15)
        {
            qualityInsights.Add("⚠️ Low Test Density Detected: Test suites represent less than 15% of codebase files.");
        }
        else
        {
            qualityInsights.Add($"✅ Strong Test Footprint: {testFiles.Count} test fixtures detected ({testRatio:P0} of C# codebase).");
        }

        var adrCount = _adrManager.GetAllAdrs().Count;
        if (adrCount < 3)
        {
            qualityInsights.Add("⚠️ Architecture Documentation Gap: Less than 3 ADRs registered for solution architecture.");
        }
        else
        {
            qualityInsights.Add($"✅ Architecture Documented: {adrCount} Architectural Decision Records on file.");
        }

        qualityInsights.Add($"🔍 Found {allPackages.Count} unique external NuGet dependencies across solution.");

        // Ingest into Knowledge Map
        foreach (var proj in discoveredProjects)
        {
            var pNodeId = $"KN-PROJ-{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
            _knowledgeMap.AddOrUpdateNode(new KnowledgeNode(
                Id: pNodeId,
                Label: proj.Name,
                Category: "Concept",
                Summary: $"{proj.ProjectType} ({proj.TargetFramework}) at path '{proj.RelativePath}'.",
                Attributes: new Dictionary<string, string>
                {
                    ["Framework"] = proj.TargetFramework,
                    ["Type"] = proj.ProjectType,
                    ["Packages"] = string.Join(", ", proj.PackageReferences.Take(5))
                }
            ));
        }

        foreach (var pat in detectedPatterns)
        {
            var patNodeId = $"KN-PAT-{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}";
            _knowledgeMap.AddOrUpdateNode(new KnowledgeNode(
                Id: patNodeId,
                Label: pat,
                Category: "Pattern",
                Summary: $"Detected architectural pattern in repository '{solutionName}'.",
                Attributes: new Dictionary<string, string> { ["Repository"] = solutionName }
            ));
        }

        // Ingest into Semantic Memory
        var memContent = $"""
        Codebase Harvest Summary for '{solutionName}':
        - Path: {directoryPath}
        - Total Files: {allFiles.Count}, C# Files: {csFiles.Count}, Test Files: {testFiles.Count}
        - Projects: {string.Join(", ", discoveredProjects.Select(p => p.Name))}
        - Key Patterns: {string.Join("; ", detectedPatterns)}
        """;

        await _memoryStore.StoreAsync(new MemoryEntry(
            Id: $"MEM-HARVEST-{Guid.NewGuid().ToString("N")[..6]}",
            Type: MemoryType.Semantic,
            Role: AgentRole.LeadArchitect,
            Content: memContent,
            Embedding: _memoryStore.GenerateEmbedding(memContent),
            Importance: 0.9f,
            Tags: new Dictionary<string, string> { ["Solution"] = solutionName, ["Type"] = "CodebaseHarvest" },
            Timestamp: DateTimeOffset.UtcNow,
            LastAccessedAt: DateTimeOffset.UtcNow
        ), cancellationToken);

        // Auto-generate Actionable Backlog if requested
        var generatedTicketIds = new List<string>();
        if (autoGenerateBacklog)
        {
            var epicTickets = _decompositionEngine.DeconstructEpic(
                $"Modernize & Harden {solutionName}",
                $"Autonomous engineering sweep to audit STRIDE security, benchmark zero-allocation hot paths, and expand unit test coverage for {solutionName}.",
                TicketPriority.High
            );
            generatedTicketIds.AddRange(epicTickets.Select(t => t.Id));
        }

        var report = new CodebaseHarvestReport(
            RootPath: directoryPath,
            SolutionName: solutionName,
            TotalFiles: allFiles.Count,
            CSharpFilesCount: csFiles.Count,
            TestFilesCount: testFiles.Count,
            DocumentationFilesCount: docFiles.Count,
            Projects: discoveredProjects,
            DetectedPatterns: detectedPatterns,
            QualityInsights: qualityInsights,
            GeneratedTicketIds: generatedTicketIds,
            ScannedAt: DateTimeOffset.UtcNow
        );

        _latestReport = report;

        // Broadcast Event
        _eventStream.Publish(AgentMessage.Create(
            role: AgentRole.LeadArchitect,
            senderName: "🔍 Codebase Harvester",
            content: $"Scanned '{solutionName}' ({discoveredProjects.Count} projects, {csFiles.Count} C# files, {detectedPatterns.Count} patterns detected). Backlog populated with {generatedTicketIds.Count} action items.",
            type: MessageType.StateChange
        ));

        return report;
    }
}
