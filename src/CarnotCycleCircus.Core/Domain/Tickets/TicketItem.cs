using System.Text.Json.Serialization;
using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;

namespace CarnotCycleCircus.Core.Domain.Tickets;

[method: JsonConstructor]
public record TicketItem(
    string Id,
    string? ProjectId,
    string? ParentEpicId,
    string Title,
    string Description,
    TicketType Type,
    TicketStatus Status,
    AgentRole AssigneeRole,
    AgentRole CreatedByRole,
    TicketPriority Priority,
    IReadOnlyList<string> DependsOnTicketIds,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<ArtifactItem> Deliverables,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt = null
)
{
    public TicketItem(
        string Id,
        string? ParentEpicId,
        string Title,
        string Description,
        TicketType Type,
        TicketStatus Status,
        AgentRole AssigneeRole,
        AgentRole CreatedByRole,
        TicketPriority Priority,
        IReadOnlyList<string> DependsOnTicketIds,
        IReadOnlyList<string> AcceptanceCriteria,
        IReadOnlyList<ArtifactItem> Deliverables,
        IReadOnlyDictionary<string, string> Metadata,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt = null)
        : this(Id, null, ParentEpicId, Title, Description, Type, Status, AssigneeRole, CreatedByRole, Priority, DependsOnTicketIds, AcceptanceCriteria, Deliverables, Metadata, CreatedAt, CompletedAt)
    {
    }

    public bool IsTerminal => Status is TicketStatus.Done;

    public bool HasDependencies => DependsOnTicketIds.Count > 0;

    public TicketItem WithProject(string? projectId) =>
        this with { ProjectId = projectId };

    public TicketItem WithStatus(TicketStatus newStatus, DateTimeOffset? completedAt = null) =>
        this with
        {
            Status = newStatus,
            CompletedAt = newStatus == TicketStatus.Done ? (completedAt ?? DateTimeOffset.UtcNow) : (newStatus != TicketStatus.Done ? null : CompletedAt)
        };

    public TicketItem WithDeliverable(ArtifactItem deliverable) =>
        this with { Deliverables = Deliverables.Append(deliverable).ToList() };

    public TicketItem WithDeliverables(IEnumerable<ArtifactItem> deliverables) =>
        this with { Deliverables = Deliverables.Concat(deliverables).ToList() };

    public TicketItem WithAssignee(AgentRole role) =>
        this with { AssigneeRole = role };

    public TicketItem WithTitle(string title) =>
        this with { Title = string.IsNullOrWhiteSpace(title) ? Title : title.Trim() };

    public TicketItem WithDescription(string description) =>
        this with { Description = description ?? string.Empty };

    public TicketItem WithPriority(TicketPriority priority) =>
        this with { Priority = priority };

    public TicketItem WithAcceptanceCriteria(IReadOnlyList<string> criteria) =>
        this with { AcceptanceCriteria = criteria ?? Array.Empty<string>() };

    public TicketItem UpdateDetails(
        string title,
        string description,
        TicketPriority priority,
        IReadOnlyList<string> criteria) =>
        this with
        {
            Title = string.IsNullOrWhiteSpace(title) ? Title : title.Trim(),
            Description = description ?? string.Empty,
            Priority = priority,
            AcceptanceCriteria = criteria ?? Array.Empty<string>()
        };
}

[method: JsonConstructor]
public record HandoffPacket(
    string HandoffId,
    string TicketId,
    string? ProjectId,
    AgentRole FromAgentRole,
    AgentRole ToAgentRole,
    IReadOnlyList<ArtifactItem> Artifacts,
    string ContextSummary,
    string ActionRequested,
    IReadOnlyList<string> ReviewChecklist,
    string? RemediationNotes,
    DateTimeOffset Timestamp
)
{
    public HandoffPacket(
        string HandoffId,
        string TicketId,
        AgentRole FromAgentRole,
        AgentRole ToAgentRole,
        IReadOnlyList<ArtifactItem> Artifacts,
        string ContextSummary,
        string ActionRequested,
        IReadOnlyList<string> ReviewChecklist,
        string? RemediationNotes,
        DateTimeOffset Timestamp)
        : this(HandoffId, TicketId, null, FromAgentRole, ToAgentRole, Artifacts, ContextSummary, ActionRequested, ReviewChecklist, RemediationNotes, Timestamp)
    {
    }

    public static HandoffPacket Create(
        string ticketId,
        AgentRole fromRole,
        AgentRole toRole,
        string contextSummary,
        string actionRequested,
        IReadOnlyList<ArtifactItem>? artifacts = null,
        IReadOnlyList<string>? reviewChecklist = null,
        string? remediationNotes = null,
        string? projectId = null) =>
        new(
            HandoffId: $"HO-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            TicketId: ticketId,
            ProjectId: projectId,
            FromAgentRole: fromRole,
            ToAgentRole: toRole,
            Artifacts: artifacts ?? Array.Empty<ArtifactItem>(),
            ContextSummary: contextSummary,
            ActionRequested: actionRequested,
            ReviewChecklist: reviewChecklist ?? Array.Empty<string>(),
            RemediationNotes: remediationNotes,
            Timestamp: DateTimeOffset.UtcNow
        );
}
