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
        artifact.ContentType.Should().Be("csharp");
        artifact.Content.Should().Contain("class RealLlmService");
    }

    [Fact]
    public async Task SimulatedScenarioEngine_WithUpstreamADR_ShouldInjectADRIntoDeveloperPrompt()
    {
        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Real Key", "sk-or-v1-real-key-12345", isActive: true);

        var ticketStore = new TicketStore();
        var teamManager = new TeamDefinitionManager();
        var inferenceResolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();

        // 1. Create parent Epic with PRD deliverable
        var epic = new TicketItem(
            Id: "EPIC-999",
            ParentEpicId: null,
            Title: "Distributed Rate Limiter",
            Description: "Token bucket rate limiter",
            Type: TicketType.Epic,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Process 1M req/sec"],
            Deliverables: [new ArtifactItem("EPIC-999_PRD.md", "# PRD: Token Bucket Algorithm Requirements", "markdown", "PRD")],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(epic);

        // 2. Create upstream Architect ticket with ADR deliverable
        var archTicket = new TicketItem(
            Id: "SUB-ARCH-999",
            ParentEpicId: "EPIC-999",
            Title: "Design ADR for Rate Limiter",
            Description: "ADR specifying IRateLimiterService and TokenBucketResult",
            Type: TicketType.Subtask,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["MADR ADR format"],
            Deliverables: [new ArtifactItem("SUB-ARCH-999_ADR.md", "# ADR-014: public interface IRateLimiterPipeline { ValueTask<bool> AllowAsync(); }", "markdown", "ADR")],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(archTicket);

        // 3. Create Developer ticket dependent on Arch ticket
        var devTicket = new TicketItem(
            Id: "SUB-DEV-999",
            ParentEpicId: "EPIC-999",
            Title: "Implement Rate Limiter Service",
            Description: "Implement IRateLimiterPipeline",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: ["SUB-ARCH-999"],
            AcceptanceCriteria: ["Implements IRateLimiterPipeline"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(devTicket);

        var engine = new SimulatedScenarioEngine(
            openRouterClient: mockClient,
            inferenceResolver: inferenceResolver,
            teamManager: teamManager,
            eventStream: eventStream,
            ticketStore: ticketStore
        );

        await engine.ExecuteRoleTaskSimulationAsync(AgentRole.SoftwareDeveloper, devTicket);

        mockClient.LastRequest.Should().NotBeNull();
        var userMessage = mockClient.LastRequest!.Messages.First(m => m.Role == "user").Content;

        // Upstream deliverables MUST be chained into prompt!
        userMessage.Should().Contain("UPSTREAM INTER-AGENT DELIVERABLE CONTEXT");
        userMessage.Should().Contain("IRateLimiterPipeline");
        userMessage.Should().Contain("Token Bucket Algorithm Requirements");
    }

    [Fact]
    public async Task SimulatedScenarioEngine_WithMultiFileCodeBlock_ShouldParseMultipleArtifacts()
    {
        var multiFileContent = """
        ```csharp:Contracts/IPubSubStream.cs
        namespace MyApp.PubSub;
        public interface IPubSubStream { }
        ```

        ```csharp:Services/PubSubStreamService.cs
        namespace MyApp.PubSub;
        public sealed class PubSubStreamService : IPubSubStream { }
        ```

        ```csharp:Tests/PubSubStreamTests.cs
        namespace MyApp.PubSub.Tests;
        public class PubSubStreamTests { }
        ```
        """;

        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Key", "sk-or-v1-custom-key", isActive: true);

        // Subclass mock to return multi-file response
        var multiMock = new MultiFileMockClient(multiFileContent);

        var engine = new SimulatedScenarioEngine(
            openRouterClient: multiMock,
            inferenceResolver: new AgentInferenceResolver(keyVault),
            teamManager: new TeamDefinitionManager(),
            eventStream: new AgentEventStream()
        );

        var ticket = new TicketItem(
            Id: "SUB-DEV-002",
            ParentEpicId: "EPIC-101",
            Title: "Implement PubSub Stream Engine",
            Description: "Deliver multi-file pub/sub engine",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Multi-file delivery"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskSimulationAsync(AgentRole.SoftwareDeveloper, ticket);

        deliverables.Should().HaveCount(3);
        deliverables.Select(d => d.Name).Should().Contain(["IPubSubStream.cs", "PubSubStreamService.cs", "PubSubStreamTests.cs"]);
    }

    [Fact]
    public async Task SimulatedScenarioEngine_WithDeveloperDeliverables_ShouldInjectCodeIntoSecurityPrompt()
    {
        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Key", "sk-or-v1-custom-key-sec", isActive: true);

        var ticketStore = new TicketStore();
        var devTicket = new TicketItem(
            Id: "SUB-DEV-005",
            ParentEpicId: "EPIC-SEC",
            Title: "Implement Crypto Vault",
            Description: "C# crypto implementation",
            Type: TicketType.Subtask,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["AES-256-GCM encryption"],
            Deliverables: [new ArtifactItem("AesGcmVaultService.cs", "public sealed class AesGcmVaultService { public void Encrypt() { } }", "csharp", "Code")],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(devTicket);

        var secTicket = new TicketItem(
            Id: "SUB-SEC-005",
            ParentEpicId: "EPIC-SEC",
            Title: "STRIDE Audit for Crypto Vault",
            Description: "Security review",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SecurityEngineer,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: ["SUB-DEV-005"],
            AcceptanceCriteria: ["STRIDE model approved"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(secTicket);

        var engine = new SimulatedScenarioEngine(
            openRouterClient: mockClient,
            inferenceResolver: new AgentInferenceResolver(keyVault),
            teamManager: new TeamDefinitionManager(),
            eventStream: new AgentEventStream(),
            ticketStore: ticketStore
        );

        await engine.ExecuteRoleTaskSimulationAsync(AgentRole.SecurityEngineer, secTicket);

        mockClient.LastRequest.Should().NotBeNull();
        var userMessage = mockClient.LastRequest!.Messages.First(m => m.Role == "user").Content;

        // Upstream C# code MUST be injected into Security review prompt!
        userMessage.Should().Contain("UPSTREAM INTER-AGENT DELIVERABLE CONTEXT");
        userMessage.Should().Contain("AesGcmVaultService.cs");
        userMessage.Should().Contain("public sealed class AesGcmVaultService");
    }

    [Fact]
    public async Task SimulatedScenarioEngine_WithDeveloperDeliverables_ShouldInjectCodeIntoQAPrompt()
    {
        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Key", "sk-or-v1-custom-key-qa", isActive: true);

        var ticketStore = new TicketStore();
        var devTicket = new TicketItem(
            Id: "SUB-DEV-006",
            ParentEpicId: "EPIC-QA",
            Title: "Implement Telemetry Stream",
            Description: "Stream implementation",
            Type: TicketType.Subtask,
            Status: TicketStatus.Done,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["99.9% uptime and unit tests"],
            Deliverables: [
                new ArtifactItem("TelemetryService.cs", "public sealed class TelemetryService { }", "csharp", "Service"),
                new ArtifactItem("TelemetryServiceTests.cs", "public class TelemetryServiceTests { [Fact] public void Test1() { } }", "csharp", "Tests")
            ],
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(devTicket);

        var qaTicket = new TicketItem(
            Id: "SUB-QA-006",
            ParentEpicId: "EPIC-QA",
            Title: "QA Acceptance Scorecard",
            Description: "QA verification",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.PrincipalQAAnalyst,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: ["SUB-DEV-006"],
            AcceptanceCriteria: ["All unit tests pass"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(qaTicket);

        var engine = new SimulatedScenarioEngine(
            openRouterClient: mockClient,
            inferenceResolver: new AgentInferenceResolver(keyVault),
            teamManager: new TeamDefinitionManager(),
            eventStream: new AgentEventStream(),
            ticketStore: ticketStore
        );

        await engine.ExecuteRoleTaskSimulationAsync(AgentRole.PrincipalQAAnalyst, qaTicket);

        mockClient.LastRequest.Should().NotBeNull();
        var userMessage = mockClient.LastRequest!.Messages.First(m => m.Role == "user").Content;

        // Upstream code and unit tests MUST be injected into QA prompt!
        userMessage.Should().Contain("UPSTREAM INTER-AGENT DELIVERABLE CONTEXT");
        userMessage.Should().Contain("TelemetryService.cs");
        userMessage.Should().Contain("TelemetryServiceTests.cs");
    }

    [Fact]
    public async Task SimulatedScenarioEngine_DeterministicFallback_ShouldProduceMultiFileCSharpBundle()
    {
        var engine = new SimulatedScenarioEngine();

        var ticket = new TicketItem(
            Id: "SUB-DEV-FALLBACK",
            ParentEpicId: "EPIC-FB",
            Title: "Implement IoT Telemetry Ingestion Pipeline",
            Description: "Deterministic fallback test",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Zero allocations", "xUnit tests included"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskSimulationAsync(AgentRole.SoftwareDeveloper, ticket);

        // Fallback should produce 4 files: Interface, Service, DI, Tests
        deliverables.Should().HaveCount(4);
        deliverables.Select(d => d.Name).Should().Contain([
            "IIoTTelemetryIngestionPipelinePipeline.cs",
            "IoTTelemetryIngestionPipelinePipelineService.cs",
            "IoTTelemetryIngestionPipelineServiceCollectionExtensions.cs",
            "IoTTelemetryIngestionPipelinePipelineTests.cs"
        ]);

        var serviceFile = deliverables.First(d => d.Name.EndsWith("Service.cs"));
        serviceFile.Content.Should().Contain("class IoTTelemetryIngestionPipelinePipelineService");
        serviceFile.Content.Should().Contain("ValueTask<IoTTelemetryIngestionPipelineResult>");
    }

    private sealed class MultiFileMockClient : IOpenRouterClient
    {
        private readonly string _content;
        public MultiFileMockClient(string content) => _content = content;

        public Task<OpenRouterChatResponse> CompleteAsync(OpenRouterChatRequest request, string apiKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OpenRouterChatResponse(
                Id: "multi-123",
                Model: request.Model,
                Choices: [new OpenRouterChoice(0, new OpenRouterMessage("assistant", _content), "stop")],
                Usage: new OpenRouterUsage(100, 100, 200)
            ));
        }

        public Task<IReadOnlyList<OpenRouterRawModelDto>> FetchModelsAsync(string? apiKey = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OpenRouterRawModelDto>>(Array.Empty<OpenRouterRawModelDto>());
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
        deliverables.Should().NotBeEmpty();
        deliverables.Should().Contain(d => d.Name == "TCK-ARCH-001_ADR.md");
        deliverables.First(d => d.Name == "TCK-ARCH-001_ADR.md").Content.Should().Contain("ADR-014");

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

    [Fact]
    public async Task SimulatedScenarioEngine_LeadArchitect_ShouldScaffoldCleanArchitectureAndAdr()
    {
        var engine = new SimulatedScenarioEngine();
        var ticket = new TicketItem(
            Id: "SUB-ARCH-99",
            ParentEpicId: "EPIC-99",
            Title: "[Arch] Design ADR & Scaffold Clean Architecture for Order Processing Engine",
            Description: "Lead Architect produces Nygard Architectural Decision Record and scaffolds Clean Architecture solution",
            Type: TicketType.Subtask,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Scaffold Clean Architecture contracts and record ADR"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var artifacts = await engine.ExecuteRoleTaskSimulationAsync(AgentRole.LeadArchitect, ticket);

        artifacts.Should().NotBeEmpty();
        artifacts.Should().Contain(a => a.Name.Contains("ADR.md"));
        artifacts.Should().Contain(a => a.Name.StartsWith("I") && a.ContentType == "csharp");
        artifacts.Should().Contain(a => a.Name.Contains("ServiceCollectionExtensions.cs"));
    }

    [Fact]
    public async Task GraphWorkflowExecutor_QA_ShouldRejectToLeadArchitect_WhenAdrIsMissing()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var memoryStore = new EmbeddedVectorMemoryStore();
        var decomp = new WorkDecompositionEngine(ticketStore);
        var router = new HandoffRouter(ticketStore, eventStream);
        var scenarioEngine = new SimulatedScenarioEngine(ticketStore: ticketStore);
        var memoryConsolidation = new MemoryConsolidationEngine(memoryStore);

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decomp,
            router,
            scenarioEngine,
            eventStream,
            memoryConsolidation
        );

        var epicId = "EPIC-TEST-NOADR";
        var epicTicket = new TicketItem(
            Id: epicId,
            ParentEpicId: null,
            Title: "No ADR Epic",
            Description: "Epic without initial ADR",
            Type: TicketType.Epic,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.TechnicalProductManager,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Deliver feature"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );
        ticketStore.CreateTicket(epicTicket);

        // Execute workflow - with initial missing ADR in first pass, QA detects and alerts failure to Lead Architect
        var result = await executor.ExecuteWorkflowAsync("No ADR Epic", "Epic without initial ADR");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RequirementsResearcher_ShouldProduceStructuredFeasibilityBrief_AndRecommendationsForTpm()
    {
        var ticketStore = new TicketStore();
        var engine = new SimulatedScenarioEngine(ticketStore: ticketStore);

        var ticket = new TicketItem(
            Id: "RES-TEST-001",
            ParentEpicId: null,
            Title: "OAuth 2.1 PKCE & Token Rotation Protocol",
            Description: "Research authorization server requirements, RFC 9068, and zero-allocation JWT tokens.",
            Type: TicketType.ResearchSpike,
            Status: TicketStatus.InProgress,
            AssigneeRole: AgentRole.RequirementsResearcher,
            CreatedByRole: AgentRole.RequirementsResearcher,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Identify relevant RFCs", "Analyze .NET 10 token libraries", "Provide TPM scope recommendations"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskSimulationAsync(AgentRole.RequirementsResearcher, ticket);

        deliverables.Should().NotBeEmpty();
        var brief = deliverables.First();
        brief.Name.Should().Be("RES-TEST-001_RESEARCH_BRIEF.md");
        brief.ContentType.Should().Be("markdown");
        brief.Content.Should().Contain("Requirements Research & Technical Feasibility Brief");
        brief.Content.Should().Contain("Standards, RFCs & Technical Specifications");
        brief.Content.Should().Contain("Recommendations for Technical Product Manager");
    }

    [Fact]
    public async Task GraphWorkflowExecutor_ShouldExecuteRequirementsResearcher_BeforeTpm_AndAttachResearchBriefToEpic()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var memoryStore = new EmbeddedVectorMemoryStore();
        var decomp = new WorkDecompositionEngine(ticketStore);
        var router = new HandoffRouter(ticketStore, eventStream);
        var scenarioEngine = new SimulatedScenarioEngine(ticketStore: ticketStore);
        var memoryConsolidation = new MemoryConsolidationEngine(memoryStore);

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decomp,
            router,
            scenarioEngine,
            eventStream,
            memoryConsolidation
        );

        var epicTitle = "High-Throughput Distributed Rate Limiter";
        var epicDesc = "Build token bucket rate limiter with zero GC allocations.";

        var success = await executor.ExecuteWorkflowAsync(epicTitle, epicDesc);
        success.Should().BeTrue();

        // Verify research node completed
        var resNode = executor.CurrentGraph.Nodes.First(n => n.Role == AgentRole.RequirementsResearcher);
        resNode.State.Should().Be(NodeExecutionState.Completed);

        // Verify Epic ticket has both Research Brief and PRD deliverables attached
        var epicTicket = ticketStore.GetAllTickets().First(t => t.Type == TicketType.Epic);
        epicTicket.Deliverables.Should().Contain(d => d.Name.EndsWith("_RESEARCH_BRIEF.md"));
        epicTicket.Deliverables.Should().Contain(d => d.Name.EndsWith("_PRD.md"));

        // Verify event stream broadcasted research banter
        eventStream.GetHistory().Should().Contain(m => m.Role == AgentRole.RequirementsResearcher && m.Content.Contains("Feasibility Brief"));
    }
}
