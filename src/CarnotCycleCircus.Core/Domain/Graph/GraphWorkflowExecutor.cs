using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tickets;
using CarnotCycleCircus.Core.Domain.Tools;

namespace CarnotCycleCircus.Core.Domain.Graph;

public interface IGraphWorkflowExecutor
{
    WorkflowGraph CurrentGraph { get; }
    bool IsRunning { get; }
    void SetGraph(WorkflowGraph graph);
    void UpdateNodePosition(string nodeId, int x, int y);
    void AddConnection(PortConnection connection);
    void RemoveConnection(string sourceNodeId, PortType sourcePort, string targetNodeId, PortType targetPort);
    void ResetGraph();

    Task<bool> ExecuteWorkflowAsync(
        string epicTitle,
        string epicDescription,
        bool triggerFailureSimulation = false,
        CancellationToken cancellationToken = default);

    Task<bool> StepNextNodeAsync(CancellationToken cancellationToken = default);

    event Action<WorkflowGraph>? OnGraphUpdated;
    event Action<string, NodeExecutionState>? OnNodeStateChanged;
}

public class GraphWorkflowExecutor : IGraphWorkflowExecutor
{
    private WorkflowGraph _graph;
    private readonly ITicketStore _ticketStore;
    private readonly IWorkDecompositionEngine _decompositionEngine;
    private readonly IHandoffRouter _handoffRouter;
    private readonly ISimulatedScenarioEngine _scenarioEngine;
    private readonly IAgentEventStream _eventStream;
    private readonly IMemoryConsolidationEngine _memoryConsolidation;
    private bool _isRunning;

    public WorkflowGraph CurrentGraph => _graph;
    public bool IsRunning => _isRunning;

    public event Action<WorkflowGraph>? OnGraphUpdated;
    public event Action<string, NodeExecutionState>? OnNodeStateChanged;

    public GraphWorkflowExecutor(
        ITicketStore ticketStore,
        IWorkDecompositionEngine decompositionEngine,
        IHandoffRouter handoffRouter,
        ISimulatedScenarioEngine scenarioEngine,
        IAgentEventStream eventStream,
        IMemoryConsolidationEngine memoryConsolidation)
    {
        _graph = WorkflowGraph.CreateDefaultEngineeringCircus();
        _ticketStore = ticketStore;
        _decompositionEngine = decompositionEngine;
        _handoffRouter = handoffRouter;
        _scenarioEngine = scenarioEngine;
        _eventStream = eventStream;
        _memoryConsolidation = memoryConsolidation;
    }

    public void SetGraph(WorkflowGraph graph)
    {
        _graph = graph;
        OnGraphUpdated?.Invoke(_graph);
    }

    public void UpdateNodePosition(string nodeId, int x, int y)
    {
        var nodes = _graph.Nodes.Select(n => n.Id == nodeId ? n with { X = x, Y = y } : n).ToList();
        _graph = _graph with { Nodes = nodes };
        OnGraphUpdated?.Invoke(_graph);
    }

    public void AddConnection(PortConnection connection)
    {
        var list = _graph.Connections.Append(connection).Distinct().ToList();
        _graph = _graph with { Connections = list };
        OnGraphUpdated?.Invoke(_graph);
    }

    public void RemoveConnection(string sourceNodeId, PortType sourcePort, string targetNodeId, PortType targetPort)
    {
        var list = _graph.Connections.Where(c => !(c.SourceNodeId == sourceNodeId && c.SourcePort == sourcePort && c.TargetNodeId == targetNodeId && c.TargetPort == targetPort)).ToList();
        _graph = _graph with { Connections = list };
        OnGraphUpdated?.Invoke(_graph);
    }

    public void ResetGraph()
    {
        var resetNodes = _graph.Nodes.Select(n => n with { State = NodeExecutionState.Idle, RetryCount = 0, LastOutputSummary = null, CurrentTicketId = null }).ToList();
        _graph = _graph with { Nodes = resetNodes };
        _isRunning = false;
        OnGraphUpdated?.Invoke(_graph);
    }

    public async Task<bool> ExecuteWorkflowAsync(
        string epicTitle,
        string epicDescription,
        bool triggerFailureSimulation = false,
        CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        ResetGraph();

        _eventStream.Publish(AgentMessage.Create(
            role: null,
            senderName: "Workflow Orchestrator",
            content: $"🚀 Starting Carnot Cycle workflow execution for Epic: '{epicTitle}' (Failure Sim: {(triggerFailureSimulation ? "Enabled" : "Disabled")})",
            type: MessageType.Alert
        ));

        // 1. TPM Phase - Work Decomposition
        var tpmNode = GetNodeByRole(AgentRole.TechnicalProductManager);
        if (tpmNode != null)
        {
            UpdateNodeState(tpmNode.Id, NodeExecutionState.Running);
            await Task.Delay(200, cancellationToken);

            var createdTickets = _decompositionEngine.DeconstructEpic(epicTitle, epicDescription);
            var epicTicket = createdTickets.First(t => t.Type == TicketType.Epic);

            _eventStream.Publish(AgentMessage.Create(
                role: AgentRole.TechnicalProductManager,
                senderName: AgentRole.TechnicalProductManager.ToDisplayName(),
                content: $"🎯 TPM Decomposed Epic into {createdTickets.Count - 1} stories & subtasks with acceptance criteria.",
                type: MessageType.Chat,
                ticketId: epicTicket.Id
            ));

            UpdateNodeState(tpmNode.Id, NodeExecutionState.Completed, $"Decomposed into {createdTickets.Count} work items.", epicTicket.Id);
        }

        // 2. Lead Architect Phase - Architecture & ADR
        var archNode = GetNodeByRole(AgentRole.LeadArchitect);
        var readyTickets = _ticketStore.GetReadyTickets();
        var archTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.LeadArchitect && t.Type == TicketType.Subtask)
            ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.LeadArchitect);

        if (archNode != null && archTicket != null)
        {
            UpdateNodeState(archNode.Id, NodeExecutionState.Running, ticketId: archTicket.Id);
            await Task.Delay(250, cancellationToken);

            var artifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.LeadArchitect, archTicket, cancellationToken);
            foreach (var a in artifacts)
            {
                archTicket = archTicket.WithDeliverable(a);
            }
            _ticketStore.UpdateTicket(archTicket);

            _handoffRouter.RouteSuccessHandoff(
                archTicket.Id,
                AgentRole.LeadArchitect,
                AgentRole.SoftwareDeveloper,
                "ADR-014 Architecture & Topology finalized.",
                "Proceed with feature implementation",
                artifacts
            );

            _handoffRouter.AdvanceWorkflowOnTicketCompletion(archTicket.Id);
            await _memoryConsolidation.ConsolidateTaskCompletionAsync(archTicket, _eventStream.GetHistory(), cancellationToken);

            UpdateNodeState(archNode.Id, NodeExecutionState.Completed, "ADR & Topology designed.", archTicket.Id);
        }

        // 3. Software Developer Phase - Implementation
        var devNode = GetNodeByRole(AgentRole.SoftwareDeveloper);
        readyTickets = _ticketStore.GetReadyTickets();
        var devTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SoftwareDeveloper && t.Type == TicketType.Subtask)
            ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SoftwareDeveloper);

        if (devNode != null && devTicket != null)
        {
            UpdateNodeState(devNode.Id, NodeExecutionState.Running, ticketId: devTicket.Id);
            await Task.Delay(250, cancellationToken);

            var devArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.SoftwareDeveloper, devTicket, cancellationToken);
            foreach (var a in devArtifacts)
            {
                devTicket = devTicket.WithDeliverable(a);
            }
            _ticketStore.UpdateTicket(devTicket);

            _handoffRouter.RouteSuccessHandoff(
                devTicket.Id,
                AgentRole.SoftwareDeveloper,
                AgentRole.SecurityEngineer,
                "Feature implemented with unit tests.",
                "Perform STRIDE review and performance audit",
                devArtifacts
            );

            _handoffRouter.AdvanceWorkflowOnTicketCompletion(devTicket.Id);
            await _memoryConsolidation.ConsolidateTaskCompletionAsync(devTicket, _eventStream.GetHistory(), cancellationToken);

            UpdateNodeState(devNode.Id, NodeExecutionState.Completed, "Implementation & unit tests delivered.", devTicket.Id);
        }

        // 4. Parallel Security & Optimization Phase
        var secNode = GetNodeByRole(AgentRole.SecurityEngineer);
        var optNode = GetNodeByRole(AgentRole.OptimizationEngineer);
        readyTickets = _ticketStore.GetReadyTickets();
        var secTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SecurityEngineer && t.Type == TicketType.Subtask)
            ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.SecurityEngineer);
        var optTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.OptimizationEngineer && t.Type == TicketType.Subtask)
            ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.OptimizationEngineer);

        if (secNode != null && secTicket != null)
        {
            UpdateNodeState(secNode.Id, NodeExecutionState.Running, ticketId: secTicket.Id);
            await Task.Delay(200, cancellationToken);

            if (triggerFailureSimulation && secNode.RetryCount == 0)
            {
                // Simulate security rejection & remediation loopback!
                UpdateNodeState(secNode.Id, NodeExecutionState.Failed, "Rejected: Secret exposure risk detected.", secTicket.Id);
                
                _handoffRouter.RouteFailureRemediation(
                    secTicket.Id,
                    AgentRole.SecurityEngineer,
                    AgentRole.SoftwareDeveloper,
                    "Unsanitized input vector in service layer.",
                    "Wrap input in ReadOnlySpan<char> and sanitize with allow-list regex."
                );

                if (devNode != null)
                {
                    UpdateNodeState(devNode.Id, NodeExecutionState.Remediating, "Fixing security vulnerability...");
                    await Task.Delay(250, cancellationToken);
                    UpdateNodeState(devNode.Id, NodeExecutionState.Completed, "Vulnerability remediated.");
                }

                UpdateNodeState(secNode.Id, NodeExecutionState.Running, ticketId: secTicket.Id);
                await Task.Delay(150, cancellationToken);
            }

            var secArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.SecurityEngineer, secTicket, cancellationToken);
            foreach (var a in secArtifacts) secTicket = secTicket.WithDeliverable(a);
            _ticketStore.UpdateTicket(secTicket);
            _handoffRouter.AdvanceWorkflowOnTicketCompletion(secTicket.Id);
            UpdateNodeState(secNode.Id, NodeExecutionState.Completed, "STRIDE Threat Model Approved.", secTicket.Id);
        }

        if (optNode != null && optTicket != null)
        {
            UpdateNodeState(optNode.Id, NodeExecutionState.Running, ticketId: optTicket.Id);
            await Task.Delay(200, cancellationToken);

            var optArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.OptimizationEngineer, optTicket, cancellationToken);
            foreach (var a in optArtifacts) optTicket = optTicket.WithDeliverable(a);
            _ticketStore.UpdateTicket(optTicket);
            _handoffRouter.AdvanceWorkflowOnTicketCompletion(optTicket.Id);
            UpdateNodeState(optNode.Id, NodeExecutionState.Completed, "Zero-Allocation & Latency Approved.", optTicket.Id);
        }

        // 5. Principal QA Phase - Validation & Certification
        var qaNode = GetNodeByRole(AgentRole.PrincipalQAAnalyst);
        readyTickets = _ticketStore.GetReadyTickets();
        var qaTicket = readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.PrincipalQAAnalyst && t.Type == TicketType.Subtask)
            ?? readyTickets.FirstOrDefault(t => t.AssigneeRole == AgentRole.PrincipalQAAnalyst);

        if (qaNode != null && qaTicket != null)
        {
            UpdateNodeState(qaNode.Id, NodeExecutionState.Running, ticketId: qaTicket.Id);
            await Task.Delay(250, cancellationToken);

            var qaArtifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(AgentRole.PrincipalQAAnalyst, qaTicket, cancellationToken);
            foreach (var a in qaArtifacts) qaTicket = qaTicket.WithDeliverable(a);
            _ticketStore.UpdateTicket(qaTicket);

            _handoffRouter.AdvanceWorkflowOnTicketCompletion(qaTicket.Id);
            await _memoryConsolidation.ConsolidateTaskCompletionAsync(qaTicket, _eventStream.GetHistory(), cancellationToken);

            UpdateNodeState(qaNode.Id, NodeExecutionState.Completed, "100% Quality Certification Scorecard.", qaTicket.Id);
        }

        // Mark remaining parent stories/epics as complete
        foreach (var remaining in _ticketStore.GetAllTickets().Where(t => t.Status != TicketStatus.Done))
        {
            _ticketStore.UpdateTicket(remaining.WithStatus(TicketStatus.Done));
        }

        _eventStream.Publish(AgentMessage.Create(
            role: null,
            senderName: "Workflow Orchestrator",
            content: $"🏆 Workflow Completed Successfully! All 6 engineering phases passed.",
            type: MessageType.Alert
        ));

        _isRunning = false;
        return true;
    }

    public async Task<bool> StepNextNodeAsync(CancellationToken cancellationToken = default)
    {
        var readyTickets = _ticketStore.GetReadyTickets();
        if (readyTickets.Count == 0) return false;

        var nextTicket = readyTickets.First();
        var node = GetNodeByRole(nextTicket.AssigneeRole);
        if (node == null) return false;

        UpdateNodeState(node.Id, NodeExecutionState.Running, ticketId: nextTicket.Id);
        await Task.Delay(150, cancellationToken);

        var artifacts = await _scenarioEngine.ExecuteRoleTaskSimulationAsync(nextTicket.AssigneeRole, nextTicket, cancellationToken);
        foreach (var a in artifacts) nextTicket = nextTicket.WithDeliverable(a);
        _ticketStore.UpdateTicket(nextTicket);

        _handoffRouter.AdvanceWorkflowOnTicketCompletion(nextTicket.Id);
        await _memoryConsolidation.ConsolidateTaskCompletionAsync(nextTicket, _eventStream.GetHistory(), cancellationToken);

        UpdateNodeState(node.Id, NodeExecutionState.Completed, $"Completed {nextTicket.Title}", nextTicket.Id);
        return true;
    }

    private GraphNode? GetNodeByRole(AgentRole role) =>
        _graph.Nodes.FirstOrDefault(n => n.Role == role);

    private void UpdateNodeState(string nodeId, NodeExecutionState state, string? outputSummary = null, string? ticketId = null)
    {
        var nodes = _graph.Nodes.Select(n => n.Id == nodeId ? n.WithState(state, outputSummary, ticketId) : n).ToList();
        _graph = _graph with { Nodes = nodes };
        OnNodeStateChanged?.Invoke(nodeId, state);
        OnGraphUpdated?.Invoke(_graph);
    }
}
