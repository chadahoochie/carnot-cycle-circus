using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Graph;

public enum NodeExecutionState
{
    Idle,
    Running,
    Completed,
    Failed,
    Remediating
}

public enum PortType
{
    Input,
    Output,
    Failure
}

public record GraphNode(
    string Id,
    AgentRole Role,
    string Name,
    int X,
    int Y,
    NodeExecutionState State = NodeExecutionState.Idle,
    int RetryCount = 0,
    string? CurrentTicketId = null,
    string? LastOutputSummary = null
)
{
    public GraphNode WithState(NodeExecutionState newState, string? outputSummary = null, string? ticketId = null) =>
        this with
        {
            State = newState,
            LastOutputSummary = outputSummary ?? LastOutputSummary,
            CurrentTicketId = ticketId ?? CurrentTicketId,
            RetryCount = newState == NodeExecutionState.Failed ? RetryCount + 1 : RetryCount
        };
}

public record PortConnection(
    string SourceNodeId,
    PortType SourcePort,
    string TargetNodeId,
    PortType TargetPort
);

public record FailurePolicy(
    int MaxRetries = 3,
    bool CircuitBreakerEnabled = true,
    AgentRole FallbackRole = AgentRole.SoftwareDeveloper
);

public record WorkflowGraph(
    string Id,
    string Name,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<PortConnection> Connections,
    FailurePolicy Policy
)
{
    public static WorkflowGraph CreateDefaultEngineeringCircus()
    {
        var nodes = new List<GraphNode>
        {
            new("node-tpm", AgentRole.TechnicalProductManager, "Technical Product Manager", 50, 100),
            new("node-arch", AgentRole.LeadArchitect, "Lead Architect", 250, 100),
            new("node-dev", AgentRole.SoftwareDeveloper, "Software Developer", 450, 100),
            new("node-sec", AgentRole.SecurityEngineer, "Security Engineer", 650, 40),
            new("node-opt", AgentRole.OptimizationEngineer, "Optimization Engineer", 650, 160),
            new("node-qa", AgentRole.PrincipalQAAnalyst, "Principal QA Analyst", 850, 100)
        };

        var connections = new List<PortConnection>
        {
            // Happy path: TPM -> Arch -> Dev -> Sec & Opt -> QA
            new("node-tpm", PortType.Output, "node-arch", PortType.Input),
            new("node-arch", PortType.Output, "node-dev", PortType.Input),
            new("node-dev", PortType.Output, "node-sec", PortType.Input),
            new("node-dev", PortType.Output, "node-opt", PortType.Input),
            new("node-sec", PortType.Output, "node-qa", PortType.Input),
            new("node-opt", PortType.Output, "node-qa", PortType.Input),

            // Failure / Reject Cables: Red Cables loop back to Dev for remediation
            new("node-sec", PortType.Failure, "node-dev", PortType.Input),
            new("node-qa", PortType.Failure, "node-dev", PortType.Input)
        };

        return new WorkflowGraph(
            Id: "graph-standard-circus",
            Name: "Standard Carnot Full-Lifecycle Orchestration Graph",
            Nodes: nodes,
            Connections: connections,
            Policy: new FailurePolicy(MaxRetries: 3, CircuitBreakerEnabled: true)
        );
    }
}
