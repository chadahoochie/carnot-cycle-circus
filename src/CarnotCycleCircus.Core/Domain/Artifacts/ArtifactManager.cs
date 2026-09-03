namespace CarnotCycleCircus.Core.Domain.Artifacts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Storage;
using CarnotCycleCircus.Core.Domain.Tickets;

public class ArtifactManager : IArtifactManager
{
    private readonly IPersistentStorageService _storageService;
    private readonly ITicketStore? _ticketStore;
    private readonly IAgentEventStream? _eventStream;

    public string ArtifactsDirectory => _storageService.Options.ArtifactsDirectory;
    public bool IsArtifactsDirectoryWriteable
    {
        get
        {
            try
            {
                if (!Directory.Exists(ArtifactsDirectory))
                {
                    Directory.CreateDirectory(ArtifactsDirectory);
                }
                var testPath = Path.Combine(ArtifactsDirectory, $".write_test_{Guid.NewGuid():N}");
                File.WriteAllText(testPath, "ok");
                File.Delete(testPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public event Action<ArtifactDescriptor>? OnArtifactExported;

    public ArtifactManager(
        IPersistentStorageService storageService,
        ITicketStore? ticketStore = null,
        IAgentEventStream? eventStream = null)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _ticketStore = ticketStore;
        _eventStream = eventStream;
    }

    public static string CategorizeArtifact(string name, string? description, AgentRole? role)
    {
        if (name.EndsWith("_RESEARCH_BRIEF.md", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("RESEARCH-", StringComparison.OrdinalIgnoreCase) ||
            (description?.Contains("Research", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (description?.Contains("Feasibility", StringComparison.OrdinalIgnoreCase) ?? false) ||
            role == AgentRole.RequirementsResearcher)
        {
            return "Research";
        }

        if (name.EndsWith("_PRD.md", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("PRD-", StringComparison.OrdinalIgnoreCase) ||
            (description?.Contains("PRD", StringComparison.OrdinalIgnoreCase) ?? false) ||
            role == AgentRole.TechnicalProductManager)
        {
            return "PRD";
        }

        if (name.EndsWith("_ADR.md", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("ADR-", StringComparison.OrdinalIgnoreCase) ||
            (description?.Contains("ADR", StringComparison.OrdinalIgnoreCase) ?? false) ||
            role == AgentRole.LeadArchitect)
        {
            return "ADR";
        }

        if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            (description?.Contains("Implementation", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (description?.Contains("Contract", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (description?.Contains("Test", StringComparison.OrdinalIgnoreCase) ?? false) ||
            role == AgentRole.SoftwareDeveloper)
        {
            return "Code";
        }

        if (name.EndsWith("_STRIDE_Model.md", StringComparison.OrdinalIgnoreCase) ||
            (description?.Contains("STRIDE", StringComparison.OrdinalIgnoreCase) ?? false) ||
            role == AgentRole.SecurityEngineer)
        {
            return "Security";
        }

        if (name.EndsWith("_Perf_Profile.md", StringComparison.OrdinalIgnoreCase) ||
            (description?.Contains("Benchmark", StringComparison.OrdinalIgnoreCase) ?? false) ||
            role == AgentRole.OptimizationEngineer)
        {
            return "Benchmark";
        }

        if (name.EndsWith("_QA_Scorecard.md", StringComparison.OrdinalIgnoreCase) ||
            (description?.Contains("QA", StringComparison.OrdinalIgnoreCase) ?? false) ||
            role == AgentRole.PrincipalQAAnalyst)
        {
            return "QA";
        }

        if (name.EndsWith("_Release_Manifest.md", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("SolutionTree.json", StringComparison.OrdinalIgnoreCase) ||
            (description?.Contains("Release", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (description?.Contains("Package", StringComparison.OrdinalIgnoreCase) ?? false) ||
            role == AgentRole.IntegrationEngineer)
        {
            return "Release";
        }

        return "General";
    }

    public string GetArtifactPath(string ticketId, string artifactName) =>
        Path.Combine(ArtifactsDirectory, "tickets", ticketId, artifactName);

    public async Task<string> SaveDeliverableArtifactAsync(TicketItem ticket, ArtifactItem deliverable, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deliverable.Name) || deliverable.Content == null)
        {
            return string.Empty;
        }

        var relativeTicketPath = $"artifacts/tickets/{ticket.Id}/{deliverable.Name}";
        await _storageService.SaveTextAsync(relativeTicketPath, deliverable.Content, cancellationToken).ConfigureAwait(false);

        var category = CategorizeArtifact(deliverable.Name, deliverable.Description, ticket.AssigneeRole);
        var categorizedFolder = category switch
        {
            "Research" => "artifacts/research",
            "PRD" => "artifacts/prds",
            "ADR" => "artifacts/adrs",
            "Code" => "artifacts/code",
            "Security" => "artifacts/security",
            "Benchmark" => "artifacts/benchmarks",
            "QA" => "artifacts/qa",
            "Release" => "artifacts/releases",
            _ => "artifacts/general"
        };

        var relativeCatPath = $"{categorizedFolder}/{deliverable.Name}";
        await _storageService.SaveTextAsync(relativeCatPath, deliverable.Content, cancellationToken).ConfigureAwait(false);

        var fullPath = Path.Combine(ArtifactsDirectory, "tickets", ticket.Id, deliverable.Name);
        var descriptor = new ArtifactDescriptor(
            Name: deliverable.Name,
            RelativePath: $"artifacts/tickets/{ticket.Id}/{deliverable.Name}",
            FullPath: fullPath,
            ContentType: deliverable.ContentType,
            Description: deliverable.Description ?? $"Deliverable for {ticket.Id}",
            Content: deliverable.Content,
            ProjectId: ticket.ProjectId,
            TicketId: ticket.Id,
            TicketTitle: ticket.Title,
            Role: ticket.AssigneeRole,
            Category: category,
            Timestamp: DateTimeOffset.UtcNow,
            SizeBytes: Encoding.UTF8.GetByteCount(deliverable.Content)
        );

        OnArtifactExported?.Invoke(descriptor);
        return fullPath;
    }

    public async Task<int> ExportAllDeliverablesAsync(CancellationToken cancellationToken = default)
    {
        if (_ticketStore == null) return 0;
        var tickets = _ticketStore.GetAllTickets();
        int count = 0;

        foreach (var t in tickets)
        {
            foreach (var del in t.Deliverables)
            {
                if (!string.IsNullOrWhiteSpace(del.Name) && del.Content != null)
                {
                    await SaveDeliverableArtifactAsync(t, del, cancellationToken).ConfigureAwait(false);
                    count++;
                }
            }
        }

        return count;
    }

    public IReadOnlyList<ArtifactDescriptor> GetAllArtifacts()
    {
        var list = new List<ArtifactDescriptor>();
        if (_ticketStore == null) return list;

        foreach (var t in _ticketStore.GetAllTickets())
        {
            foreach (var del in t.Deliverables)
            {
                if (string.IsNullOrWhiteSpace(del.Name) || del.Content == null) continue;
                var category = CategorizeArtifact(del.Name, del.Description, t.AssigneeRole);
                var fullPath = Path.Combine(ArtifactsDirectory, "tickets", t.Id, del.Name);
                list.Add(new ArtifactDescriptor(
                    Name: del.Name,
                    RelativePath: $"artifacts/tickets/{t.Id}/{del.Name}",
                    FullPath: fullPath,
                    ContentType: del.ContentType,
                    Description: del.Description ?? $"Deliverable for {t.Id}",
                    Content: del.Content,
                    ProjectId: t.ProjectId,
                    TicketId: t.Id,
                    TicketTitle: t.Title,
                    Role: t.AssigneeRole,
                    Category: category,
                    Timestamp: t.CompletedAt ?? t.CreatedAt,
                    SizeBytes: Encoding.UTF8.GetByteCount(del.Content)
                ));
            }
        }

        return list.OrderByDescending(a => a.Timestamp).ToList();
    }

    public IReadOnlyList<ArtifactDescriptor> GetByProject(string projectId) =>
        GetAllArtifacts().Where(a => string.Equals(a.ProjectId, projectId, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<ArtifactDescriptor> GetArtifactsByTicket(string ticketId) =>
        GetAllArtifacts().Where(a => string.Equals(a.TicketId, ticketId, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<ArtifactDescriptor> GetArtifactsByCategory(string category) =>
        GetAllArtifacts().Where(a => string.Equals(a.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<ArtifactDescriptor> GetArtifactsByRole(AgentRole role) =>
        GetAllArtifacts().Where(a => a.Role == role).ToList();

    public Task<string?> ReadArtifactContentAsync(string relativePath, CancellationToken cancellationToken = default) =>
        _storageService.LoadTextAsync(relativePath, cancellationToken);
}
