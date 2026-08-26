namespace CarnotCycleCircus.Core.Domain.Artifacts;

using CarnotCycleCircus.Core.Domain.Agents;

public record ArtifactDescriptor(
    string Name,
    string RelativePath,
    string FullPath,
    string ContentType,
    string Description,
    string Content,
    string? TicketId,
    string? TicketTitle,
    AgentRole? Role,
    string Category,
    DateTimeOffset Timestamp,
    long SizeBytes
);
