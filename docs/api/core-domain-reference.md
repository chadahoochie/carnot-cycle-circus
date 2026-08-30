# Core Domain & API Reference 📚🔍

This document provides a comprehensive technical reference of all primary interfaces, domain models, value objects, and service contracts in **`CarnotCycleCircus.Core`**.

---

## 1. Domain: Agents & Teams (`CarnotCycleCircus.Core.Domain.Agents` & `Teams`)

### `AgentRole` (Enum)
```csharp
public enum AgentRole
{
    RequirementsResearcher,
    TechnicalProductManager,
    LeadArchitect,
    SoftwareDeveloper,
    SecurityEngineer,
    OptimizationEngineer,
    PrincipalQAAnalyst,
    IntegrationEngineer
}
```

### `AgentPersona` (Record)
Represents the persona, prompt instructions, model settings, and tool permissions of an agent.
```csharp
public record AgentPersona(
    AgentRole Role,
    string Name,
    string SystemPrompt,
    string DefaultModel,
    string FallbackModel,
    double Temperature,
    IReadOnlyList<string> AllowedToolNames
);
```

### `ITeamDefinitionManager` (Interface)
Manages team compositions, saved configurations, and pre-built archetypes.
```csharp
public interface ITeamDefinitionManager
{
    IReadOnlyList<TeamDefinition> GetAllTeams();
    TeamDefinition? GetTeam(string id);
    TeamDefinition SaveTeam(TeamDefinition team);
    bool DeleteTeam(string id);
    TeamDefinition LoadArchetype(string archetypeName);
    string ExportToJson(string teamId);
    TeamDefinition ImportFromJson(string json);
    EngineeringTeam GetCurrentTeam();
    void SetCurrentTeam(TeamDefinition team);
    event Action<EngineeringTeam>? OnCurrentTeamChanged;
}
```

---

## 2. Domain: Tickets & Work Decomposition (`CarnotCycleCircus.Core.Domain.Tickets`)

### `TicketItem` (Record)
```csharp
public record TicketItem(
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
    DateTimeOffset? CompletedAt = null
)
{
    public bool IsTerminal => Status is TicketStatus.Done;
    public bool HasDependencies => DependsOnTicketIds.Count > 0;

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
}
```

### `HandoffPacket` (Record)
Formal payload emitted across agent boundaries.
```csharp
public record HandoffPacket(
    string HandoffId,
    string TicketId,
    AgentRole FromAgentRole,
    AgentRole ToAgentRole,
    IReadOnlyList<ArtifactItem> Artifacts,
    string ContextSummary,
    string ActionRequested,
    IReadOnlyList<string> ReviewChecklist,
    string? RemediationNotes,
    DateTimeOffset Timestamp
);
```

### `ITicketStore` (Interface)
Thread-safe repository for tickets and inter-agent handoff history.
```csharp
public interface ITicketStore
{
    IReadOnlyList<TicketItem> GetAllTickets();
    TicketItem? GetTicketById(string id);
    IReadOnlyList<TicketItem> GetTicketsByEpic(string parentEpicId);
    IReadOnlyList<TicketItem> GetTicketsByStatus(TicketStatus status);
    IReadOnlyList<TicketItem> GetTicketsByAssignee(AgentRole role);
    TicketItem CreateTicket(TicketItem ticket);
    TicketItem UpdateTicket(TicketItem ticket);
    bool DeleteTicket(string id);
    bool AreDependenciesSatisfied(string ticketId);
    IReadOnlyList<TicketItem> GetReadyTickets();
    void RecordHandoff(HandoffPacket handoff);
    IReadOnlyList<HandoffPacket> GetHandoffsForTicket(string ticketId);
    IReadOnlyList<HandoffPacket> GetAllHandoffs();
    void Clear();
    event Action<TicketItem>? OnTicketChanged;
    event Action<HandoffPacket>? OnHandoffRecorded;
}
```

### `IWorkDecompositionEngine` (Interface)
```csharp
public interface IWorkDecompositionEngine
{
    IReadOnlyList<TicketItem> DeconstructEpic(string epicTitle, string epicDescription, TicketPriority priority = TicketPriority.High);
    IReadOnlyList<TicketItem> DeconstructStoryIntoTechnicalSubtasks(TicketItem userStory);
}
```

### `IHandoffRouter` (Interface)
```csharp
public interface IHandoffRouter
{
    HandoffPacket RouteSuccessHandoff(string ticketId, AgentRole fromRole, AgentRole toRole, string summary, string actionRequested, IReadOnlyList<ArtifactItem>? deliverables = null);
    HandoffPacket RouteFailureRemediation(string ticketId, AgentRole rejectingRole, AgentRole remediationRole, string rejectionReason, string remediationInstructions);
    IReadOnlyList<TicketItem> AdvanceWorkflowOnTicketCompletion(string completedTicketId);
}
```

---

## 3. Domain: Hierarchical Memory (`CarnotCycleCircus.Core.Domain.Memory`)

### `IPersistentMemoryStore` (Interface)
```csharp
public interface IPersistentMemoryStore
{
    Task StoreAsync(MemoryEntry entry, CancellationToken cancellationToken = default);
    Task<MemoryEntry?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryEntry>> GetByTypeAsync(MemoryType type, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryEntry>> GetByRoleAsync(AgentRole role, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(string query, int topK = 5, MemoryType? typeFilter = null, AgentRole? roleFilter = null, CancellationToken cancellationToken = default);
    Task<int> PruneAsync(float minImportanceThreshold, TimeSpan olderThan, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryEntry>> GetAllAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<float> GenerateEmbedding(string text);
}
```

---

## 4. Domain: Workflow Graph & Execution (`CarnotCycleCircus.Core.Domain.Graph`)

### `IGraphWorkflowExecutor` (Interface)
```csharp
public interface IGraphWorkflowExecutor
{
    WorkflowGraph CurrentGraph { get; }
    bool IsRunning { get; }
    void SetGraph(WorkflowGraph graph);
    void UpdateNodePosition(string nodeId, int x, int y);
    void AddConnection(PortConnection connection);
    void RemoveConnection(string sourceNodeId, PortType sourcePort, string targetNodeId, PortType targetPort);
    void ResetGraph();
    Task<bool> ExecuteWorkflowAsync(string epicTitle, string epicDescription, bool triggerFailureSimulation = false, CancellationToken cancellationToken = default);
    Task<bool> StepNextNodeAsync(CancellationToken cancellationToken = default);
    event Action<WorkflowGraph>? OnGraphUpdated;
    event Action<string, NodeExecutionState>? OnNodeStateChanged;
}
```

---

## 5. Domain: Inference & Key Vault (`CarnotCycleCircus.Core.Domain.Inference`)

### `IApiKeyVaultService` (Interface)
```csharp
public interface IApiKeyVaultService
{
    IReadOnlyList<ApiKeyVaultEntry> GetAllKeys();
    ApiKeyVaultEntry? GetKey(string keyId);
    ApiKeyVaultEntry? GetActiveKey();
    ApiKeyVaultEntry AddOrUpdateKey(string keyName, string rawApiKey, bool isActive = true);
    bool DeleteKey(string keyId);
    void SetActiveKey(string keyId);
    Task<bool> TestKeyConnectionAsync(string rawApiKey, CancellationToken cancellationToken = default);
    event Action<ApiKeyVaultEntry>? OnKeyUpdated;
}
```

### `IOpenRouterClient` (Interface)
```csharp
public interface IOpenRouterClient
{
    Task<OpenRouterChatResponse> CompleteAsync(OpenRouterChatRequest request, string apiKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpenRouterRawModelDto>> FetchModelsAsync(string? apiKey = null, CancellationToken cancellationToken = default);
}
```

### `IAgentExecutionEngine` (Interface)
Coordinates live inference, autonomous syntax self-healing, inter-agent context injection, and deliverable parsing.
```csharp
public interface IAgentExecutionEngine
{
    Task<IReadOnlyList<ArtifactItem>> ExecuteRoleTaskAsync(AgentRole role, TicketItem ticket, CancellationToken cancellationToken = default);
}
```

---

## 6. Domain: Deliverables & Artifacts (`CarnotCycleCircus.Core.Domain.Artifacts`)

### `ArtifactDescriptor` (Record)
```csharp
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
```

### `IArtifactManager` (Interface)
```csharp
public interface IArtifactManager
{
    string ArtifactsDirectory { get; }
    bool IsArtifactsDirectoryWriteable { get; }
    
    IReadOnlyList<ArtifactDescriptor> GetAllArtifacts();
    IReadOnlyList<ArtifactDescriptor> GetArtifactsByTicket(string ticketId);
    IReadOnlyList<ArtifactDescriptor> GetArtifactsByCategory(string category);
    IReadOnlyList<ArtifactDescriptor> GetArtifactsByRole(AgentRole role);

    Task<string> SaveDeliverableArtifactAsync(TicketItem ticket, ArtifactItem deliverable, CancellationToken cancellationToken = default);
    Task<int> ExportAllDeliverablesAsync(CancellationToken cancellationToken = default);
    Task<string?> ReadArtifactContentAsync(string relativePath, CancellationToken cancellationToken = default);
    string GetArtifactPath(string ticketId, string artifactName);

    event Action<ArtifactDescriptor>? OnArtifactExported;
}
```

---

## 7. Domain: Standards, ADRs, Knowledge, & Tools

### `IStandardsValidator` (`CarnotCycleCircus.Core.Domain.Standards`)
```csharp
public interface IStandardsValidator
{
    EngineeringStandardsProfile CurrentProfile { get; set; }
    ValidationResult ValidateTicketForCompletion(TicketItem ticket);
}
```

### `IAdrDocumentManager` (`CarnotCycleCircus.Core.Domain.Docs`)
```csharp
public interface IAdrDocumentManager
{
    IReadOnlyList<ArchitecturalDecisionRecord> GetAllAdrs();
    ArchitecturalDecisionRecord? GetAdr(string id);
    ArchitecturalDecisionRecord SaveAdr(ArchitecturalDecisionRecord adr);
    bool DeleteAdr(string id);
    IReadOnlyList<ProjectDocument> GetAllDocs();
    ProjectDocument? GetDoc(string id);
    ProjectDocument SaveDoc(ProjectDocument doc);
    bool DeleteDoc(string id);
    string ExportCompleteMarkdownBundle();
}
```

### `IKnowledgeMapService` (`CarnotCycleCircus.Core.Domain.Knowledge`)
```csharp
public interface IKnowledgeMapService
{
    KnowledgeMap GetFullMap();
    KnowledgeNode? GetNode(string id);
    KnowledgeNode AddOrUpdateNode(KnowledgeNode node);
    bool DeleteNode(string id);
    void AddEdge(string sourceId, string targetId, string relationship);
    bool RemoveEdge(string sourceId, string targetId, string relationship);
    IReadOnlyList<KnowledgeNode> SearchNodes(string query);
    string ExtractSubGraphContext(string conceptQuery);
}
```

### `IToolDefinition` (`CarnotCycleCircus.Core.Domain.Tools`)
```csharp
public interface IToolDefinition
{
    string Name { get; }
    string Description { get; }
    IReadOnlyDictionary<string, string> ParameterSchema { get; }
    Task<ToolResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default);
}
```

### `IAgentEventStream` (`CarnotCycleCircus.Core.Domain.Events`)
```csharp
public interface IAgentEventStream
{
    void Publish(AgentMessage message);
    IReadOnlyList<AgentMessage> GetHistory();
    void Clear();
    event Action<AgentMessage>? OnMessagePublished;
}
```
