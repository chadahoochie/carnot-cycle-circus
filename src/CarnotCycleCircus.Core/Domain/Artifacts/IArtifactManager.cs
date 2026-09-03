namespace CarnotCycleCircus.Core.Domain.Artifacts;

using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Tickets;

public interface IArtifactManager
{
    string ArtifactsDirectory { get; }
    bool IsArtifactsDirectoryWriteable { get; }
    
    IReadOnlyList<ArtifactDescriptor> GetAllArtifacts();
    IReadOnlyList<ArtifactDescriptor> GetByProject(string projectId);
    IReadOnlyList<ArtifactDescriptor> GetArtifactsByTicket(string ticketId);
    IReadOnlyList<ArtifactDescriptor> GetArtifactsByCategory(string category);
    IReadOnlyList<ArtifactDescriptor> GetArtifactsByRole(AgentRole role);

    Task<string> SaveDeliverableArtifactAsync(TicketItem ticket, ArtifactItem deliverable, CancellationToken cancellationToken = default);
    Task<int> ExportAllDeliverablesAsync(CancellationToken cancellationToken = default);
    Task<string?> ReadArtifactContentAsync(string relativePath, CancellationToken cancellationToken = default);
    string GetArtifactPath(string ticketId, string artifactName);

    event Action<ArtifactDescriptor>? OnArtifactExported;
}
