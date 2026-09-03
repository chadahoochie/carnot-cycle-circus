namespace CarnotCycleCircus.Core.Domain.Artifacts;

using System.Text.Json.Serialization;
using CarnotCycleCircus.Core.Domain.Agents;

[method: JsonConstructor]
public record ArtifactDescriptor(
    string Name,
    string RelativePath,
    string FullPath,
    string ContentType,
    string Description,
    string Content,
    string? ProjectId,
    string? TicketId,
    string? TicketTitle,
    AgentRole? Role,
    string Category,
    DateTimeOffset Timestamp,
    long SizeBytes
)
{
    public ArtifactDescriptor(
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
        long SizeBytes)
        : this(Name, RelativePath, FullPath, ContentType, Description, Content, null, TicketId, TicketTitle, Role, Category, Timestamp, SizeBytes)
    {
    }
}
