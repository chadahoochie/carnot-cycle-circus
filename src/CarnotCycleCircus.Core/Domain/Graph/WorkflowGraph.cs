using CarnotCycleCircus.Core.Domain.Agents;

namespace CarnotCycleCircus.Core.Domain.Graph;

public enum NodeExecutionState
{
    Idle,
    Running,
    Completed,
    Failed,
    Remediating,
    WaitingForApproval
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
    string? LastOutputSummary = null,
    string? AgentId = null
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
            new("node-res", AgentRole.RequirementsResearcher, "Requirements Researcher", 30, 100, AgentId: "agent-requirementsresearcher"),
            new("node-tpm", AgentRole.TechnicalProductManager, "Technical Product Manager", 170, 100, AgentId: "agent-technicalproductmanager"),
            new("node-arch", AgentRole.LeadArchitect, "Lead Architect", 310, 100, AgentId: "agent-leadarchitect"),
            new("node-dev", AgentRole.SoftwareDeveloper, "Software Developer", 450, 100, AgentId: "agent-softwaredeveloper"),
            new("node-sec", AgentRole.SecurityEngineer, "Security Engineer", 600, 40, AgentId: "agent-securityengineer"),
            new("node-opt", AgentRole.OptimizationEngineer, "Optimization Engineer", 600, 160, AgentId: "agent-optimizationengineer"),
            new("node-qa", AgentRole.PrincipalQAAnalyst, "Principal QA Analyst", 750, 100, AgentId: "agent-principalqaanalyst"),
            new("node-int", AgentRole.IntegrationEngineer, "Integration Engineer", 910, 100, AgentId: "agent-integrationengineer")
        };

        var connections = new List<PortConnection>
        {
            // Happy path: Res -> TPM -> Arch -> Dev -> Sec & Opt -> QA -> Integration
            new("node-res", PortType.Output, "node-tpm", PortType.Input),
            new("node-tpm", PortType.Output, "node-arch", PortType.Input),
            new("node-arch", PortType.Output, "node-dev", PortType.Input),
            new("node-dev", PortType.Output, "node-sec", PortType.Input),
            new("node-dev", PortType.Output, "node-opt", PortType.Input),
            new("node-sec", PortType.Output, "node-qa", PortType.Input),
            new("node-opt", PortType.Output, "node-qa", PortType.Input),
            new("node-qa", PortType.Output, "node-int", PortType.Input),

            // Failure / Reject Cables: Red Cables loop back for remediation
            new("node-tpm", PortType.Failure, "node-res", PortType.Input),
            new("node-sec", PortType.Failure, "node-dev", PortType.Input),
            new("node-qa", PortType.Failure, "node-dev", PortType.Input),
            new("node-qa", PortType.Failure, "node-arch", PortType.Input),
            new("node-int", PortType.Failure, "node-dev", PortType.Input),
            new("node-int", PortType.Failure, "node-arch", PortType.Input)
        };

        return new WorkflowGraph(
            Id: "graph-standard-circus",
            Name: "Standard Carnot Full-Lifecycle Orchestration Graph",
            Nodes: nodes,
            Connections: connections,
            Policy: new FailurePolicy(MaxRetries: 3, CircuitBreakerEnabled: true)
        );
    }

    public static WorkflowGraph CreateRapidPrototype()
    {
        var nodes = new List<GraphNode>
        {
            new("node-tpm", AgentRole.TechnicalProductManager, "Technical Product Manager", 100, 100, AgentId: "agent-technicalproductmanager"),
            new("node-dev", AgentRole.SoftwareDeveloper, "Software Developer", 400, 100, AgentId: "agent-softwaredeveloper"),
            new("node-qa", AgentRole.PrincipalQAAnalyst, "Principal QA Analyst", 700, 100, AgentId: "agent-principalqaanalyst")
        };

        var connections = new List<PortConnection>
        {
            new("node-tpm", PortType.Output, "node-dev", PortType.Input),
            new("node-dev", PortType.Output, "node-qa", PortType.Input),
            new("node-qa", PortType.Failure, "node-dev", PortType.Input)
        };

        return new WorkflowGraph(
            Id: "graph-rapid-prototype",
            Name: "Rapid Prototype Fast-Track Graph",
            Nodes: nodes,
            Connections: connections,
            Policy: new FailurePolicy(MaxRetries: 2, CircuitBreakerEnabled: true, FallbackRole: AgentRole.SoftwareDeveloper)
        );
    }

    public static WorkflowGraph CreateZeroTrustSecurityCircus()
    {
        var nodes = new List<GraphNode>
        {
            new("node-tpm", AgentRole.TechnicalProductManager, "Technical Product Manager", 50, 100, AgentId: "agent-technicalproductmanager"),
            new("node-arch", AgentRole.LeadArchitect, "Lead Architect", 250, 100, AgentId: "agent-leadarchitect"),
            new("node-dev", AgentRole.SoftwareDeveloper, "Software Developer", 450, 100, AgentId: "agent-softwaredeveloper"),
            new("node-sec", AgentRole.SecurityEngineer, "Security Engineer", 650, 100, AgentId: "agent-securityengineer"),
            new("node-qa", AgentRole.PrincipalQAAnalyst, "Principal QA Analyst", 850, 100, AgentId: "agent-principalqaanalyst")
        };

        var connections = new List<PortConnection>
        {
            new("node-tpm", PortType.Output, "node-arch", PortType.Input),
            new("node-arch", PortType.Output, "node-dev", PortType.Input),
            new("node-dev", PortType.Output, "node-sec", PortType.Input),
            new("node-sec", PortType.Output, "node-qa", PortType.Input),
            new("node-sec", PortType.Failure, "node-dev", PortType.Input),
            new("node-qa", PortType.Failure, "node-sec", PortType.Input)
        };

        return new WorkflowGraph(
            Id: "graph-zero-trust",
            Name: "Zero-Trust Security Gated Graph",
            Nodes: nodes,
            Connections: connections,
            Policy: new FailurePolicy(MaxRetries: 5, CircuitBreakerEnabled: true, FallbackRole: AgentRole.SecurityEngineer)
        );
    }

    public static WorkflowGraph CreateHighPerformanceCircus()
    {
        var nodes = new List<GraphNode>
        {
            new("node-tpm", AgentRole.TechnicalProductManager, "Technical Product Manager", 50, 100, AgentId: "agent-technicalproductmanager"),
            new("node-dev", AgentRole.SoftwareDeveloper, "Software Developer", 300, 100, AgentId: "agent-softwaredeveloper"),
            new("node-opt", AgentRole.OptimizationEngineer, "Optimization Engineer", 550, 100, AgentId: "agent-optimizationengineer"),
            new("node-qa", AgentRole.PrincipalQAAnalyst, "Principal QA Analyst", 800, 100, AgentId: "agent-principalqaanalyst")
        };

        var connections = new List<PortConnection>
        {
            new("node-tpm", PortType.Output, "node-dev", PortType.Input),
            new("node-dev", PortType.Output, "node-opt", PortType.Input),
            new("node-opt", PortType.Output, "node-qa", PortType.Input),
            new("node-opt", PortType.Failure, "node-dev", PortType.Input),
            new("node-qa", PortType.Failure, "node-opt", PortType.Input)
        };

        return new WorkflowGraph(
            Id: "graph-high-performance",
            Name: "High-Performance Zero-Allocation Graph",
            Nodes: nodes,
            Connections: connections,
            Policy: new FailurePolicy(MaxRetries: 3, CircuitBreakerEnabled: true, FallbackRole: AgentRole.OptimizationEngineer)
        );
    }
}
