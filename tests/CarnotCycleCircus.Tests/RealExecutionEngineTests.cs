using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Graph;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class RealExecutionEngineTests
{
    private sealed class MockOpenRouterClient : IOpenRouterClient
    {
        public OpenRouterChatRequest? LastRequest { get; private set; }
        public string? LastApiKey { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task<OpenRouterChatResponse> CompleteAsync(
            OpenRouterChatRequest request,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new HttpRequestException("OpenRouter API returned 401 Unauthorized: Invalid API key.");
            }

            LastRequest = request;
            LastApiKey = apiKey;

            var codeContent = """
            namespace CarnotCycleCircus.Services;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class RealLlmService
            {
                public async ValueTask<bool> ExecuteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
                {
                    await Task.Yield();
                    return true;
                }
            }
            """;

            var response = new OpenRouterChatResponse(
                Id: "gen-12345",
                Model: request.Model,
                Choices: [
                    new OpenRouterChoice(0, new OpenRouterMessage("assistant", $"```csharp\n{codeContent}\n```"), "stop")
                ],
                Usage: new OpenRouterUsage(200, 150, 350)
            );

            return Task.FromResult(response);
        }

        public Task<IReadOnlyList<OpenRouterRawModelDto>> FetchModelsAsync(
            string? apiKey = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<OpenRouterRawModelDto> list = Array.Empty<OpenRouterRawModelDto>();
            return Task.FromResult(list);
        }
    }

    [Fact]
    public async Task SimulatedScenarioEngine_WithRealOpenRouterKey_ShouldCallOpenRouterAndProduceDeliverable()
    {
        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Real Production Key", "sk-or-v1-9876543210fedcba", isActive: true);

        var teamManager = new TeamDefinitionManager();
        var inferenceResolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();

        var engine = new SimulatedScenarioEngine(
            openRouterClient: mockClient,
            inferenceResolver: inferenceResolver,
            teamManager: teamManager,
            eventStream: eventStream
        );

        var ticket = new TicketItem(
            Id: "TCK-DEV-001",
            ParentEpicId: "EPIC-100",
            Title: "High-Throughput Raft Buffer Pipeline",
            Description: "Implement zero-allocation memory pipeline",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Zero Gen0 heap allocations"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskSimulationAsync(AgentRole.SoftwareDeveloper, ticket);

        deliverables.Should().HaveCount(1);
        mockClient.LastRequest.Should().NotBeNull();
        mockClient.LastApiKey.Should().Be("sk-or-v1-9876543210fedcba");

        var artifact = deliverables[0];
        artifact.Name.Should().Be("TCK-DEV-001_Implementation.cs");
        artifact.ContentType.Should().Be("csharp");
        artifact.Content.Should().Contain("class RealLlmService");
    }

    [Fact]
    public async Task SimulatedScenarioEngine_WhenOpenRouterFails_ShouldFallBackToDeterministicAndPublishAlert()
    {
        var mockClient = new MockOpenRouterClient { ShouldThrow = true };
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Real Key But Expired", "sk-or-v1-real-key-expired", isActive: true);

        var teamManager = new TeamDefinitionManager();
        var inferenceResolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();

        var engine = new SimulatedScenarioEngine(
            openRouterClient: mockClient,
            inferenceResolver: inferenceResolver,
            teamManager: teamManager,
            eventStream: eventStream
        );

        var ticket = new TicketItem(
            Id: "TCK-ARCH-001",
            ParentEpicId: "EPIC-100",
            Title: "ADR for Zero Trust Security",
            Description: "Design ADR for zero-trust security boundary",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["MADR compliant ADR"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskSimulationAsync(AgentRole.LeadArchitect, ticket);

        // Should cleanly return fallback deliverables without unhandled exception
        deliverables.Should().HaveCount(1);
        deliverables[0].Name.Should().Be("TCK-ARCH-001_ADR.md");
        deliverables[0].Content.Should().Contain("ADR-014");

        // Event stream should capture the alert
        eventStream.GetHistory().Should().Contain(m => m.Type == MessageType.Alert && m.Content.Contains("OpenRouter API error"));
    }

    [Fact]
    public async Task GraphWorkflowExecutor_ExecuteTicketAsync_ShouldExecuteSingleTicketAndAdvanceDependencies()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var handoffRouter = new HandoffRouter(ticketStore, eventStream);
        var scenarioEngine = new SimulatedScenarioEngine();
        var memoryStore = new EmbeddedVectorMemoryStore();
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);
        var decompositionEngine = new WorkDecompositionEngine(ticketStore);

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decompositionEngine,
            handoffRouter,
            scenarioEngine,
            eventStream,
            consolidationEngine
        );

        // Create 2 dependent tickets
        var archTicket = new TicketItem(
            Id: "TCK-ARCH-01",
            ParentEpicId: "EPIC-TEST",
            Title: "Design Architecture",
            Description: "ADR design",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Architecture approved"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var devTicket = new TicketItem(
            Id: "TCK-DEV-01",
            ParentEpicId: "EPIC-TEST",
            Title: "Implement Feature",
            Description: "Code implementation",
            Type: TicketType.Subtask,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: ["TCK-ARCH-01"],
            AcceptanceCriteria: ["Compiles cleanly"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        ticketStore.CreateTicket(archTicket);
        ticketStore.CreateTicket(devTicket);

        // Initially dev is not ready
        ticketStore.AreDependenciesSatisfied(devTicket.Id).Should().BeFalse();

        // Execute Arch ticket
        var result = await executor.ExecuteTicketAsync("TCK-ARCH-01");
        result.Should().BeTrue();

        var completedArch = ticketStore.GetTicketById("TCK-ARCH-01");
        completedArch!.Status.Should().Be(TicketStatus.Done);
        completedArch.Deliverables.Should().NotBeEmpty();

        // Dev ticket should now automatically be Ready!
        var updatedDev = ticketStore.GetTicketById("TCK-DEV-01");
        updatedDev!.Status.Should().Be(TicketStatus.Ready);
        ticketStore.AreDependenciesSatisfied("TCK-DEV-01").Should().BeTrue();
    }

    [Fact]
    public async Task GraphWorkflowExecutor_ExecuteReadyTicketsAsync_ShouldNotSweepUnrelatedBacklogTickets()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var handoffRouter = new HandoffRouter(ticketStore, eventStream);
        var scenarioEngine = new SimulatedScenarioEngine();
        var memoryStore = new EmbeddedVectorMemoryStore();
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);
        var decompositionEngine = new WorkDecompositionEngine(ticketStore);

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decompositionEngine,
            handoffRouter,
            scenarioEngine,
            eventStream,
            consolidationEngine
        );

        // Ready ticket
        var readyTicket = new TicketItem(
            Id: "TCK-READY-01",
            ParentEpicId: null,
            Title: "Active Task",
            Description: "Active task description",
            Type: TicketType.Feature,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Criteria 1"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        // Unrelated backlog ticket with unmet dependency
        var backlogTicket = new TicketItem(
            Id: "TCK-FUTURE-01",
            ParentEpicId: null,
            Title: "Future Unrelated Backlog",
            Description: "Should remain untouched in backlog",
            Type: TicketType.Feature,
            Status: TicketStatus.Backlog,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.Low,
            DependsOnTicketIds: ["TCK-NONEXISTENT-DEP"],
            AcceptanceCriteria: ["Do not sweep"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        ticketStore.CreateTicket(readyTicket);
        ticketStore.CreateTicket(backlogTicket);

        // Run ready tickets
        var executed = await executor.ExecuteReadyTicketsAsync();
        executed.Should().BeTrue();

        // Ready ticket is now Done
        ticketStore.GetTicketById("TCK-READY-01")!.Status.Should().Be(TicketStatus.Done);

        // Backlog ticket MUST NOT BE SWEPT TO DONE
        var preservedBacklog = ticketStore.GetTicketById("TCK-FUTURE-01");
        preservedBacklog!.Status.Should().Be(TicketStatus.Backlog);
    }
}
