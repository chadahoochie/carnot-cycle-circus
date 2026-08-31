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
    [Fact]
    public async Task AgentExecutionEngine_WithRealOpenRouterKey_ShouldCallOpenRouterAndProduceDeliverable()
    {
        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Real Production Key", "sk-or-v1-9876543210fedcba", isActive: true);

        var teamManager = new TeamDefinitionManager();
        var inferenceResolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();

        var engine = new AgentExecutionEngine(
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

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.SoftwareDeveloper, ticket);

        deliverables.Should().HaveCount(1);
        mockClient.LastRequest.Should().NotBeNull();
        mockClient.LastApiKey.Should().Be("sk-or-v1-9876543210fedcba");

        var artifact = deliverables[0];
        artifact.ContentType.Should().Be("csharp");
        artifact.Content.Should().Contain("class Service");
    }

    [Fact]
    public async Task AgentExecutionEngine_WithUpstreamADR_ShouldInjectADRIntoDeveloperPrompt()
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

        var engine = new AgentExecutionEngine(
            openRouterClient: mockClient,
            inferenceResolver: inferenceResolver,
            teamManager: teamManager,
            eventStream: eventStream,
            ticketStore: ticketStore
        );

        await engine.ExecuteRoleTaskAsync(AgentRole.SoftwareDeveloper, devTicket);

        mockClient.LastRequest.Should().NotBeNull();
        var userMessage = mockClient.LastRequest!.Messages.First(m => m.Role == "user").Content;

        // Upstream deliverables MUST be chained into prompt!
        userMessage.Should().Contain("UPSTREAM INTER-AGENT DELIVERABLE CONTEXT");
        userMessage.Should().Contain("IRateLimiterPipeline");
        userMessage.Should().Contain("Token Bucket Algorithm Requirements");
    }

    [Fact]
    public async Task AgentExecutionEngine_WithMultiFileCodeBlock_ShouldParseMultipleArtifacts()
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

        var mockClient = new MockOpenRouterClient
        {
            ResponseFactory = _ => multiFileContent
        };
        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Key", "sk-or-v1-custom-key", isActive: true);

        var engine = new AgentExecutionEngine(
            openRouterClient: mockClient,
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

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.SoftwareDeveloper, ticket);

        deliverables.Should().HaveCount(3);
        deliverables.Select(d => d.Name).Should().Contain(["IPubSubStream.cs", "PubSubStreamService.cs", "PubSubStreamTests.cs"]);
    }

    [Fact]
    public async Task AgentExecutionEngine_WithDeveloperDeliverables_ShouldInjectCodeIntoSecurityPrompt()
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

        var engine = new AgentExecutionEngine(
            openRouterClient: mockClient,
            inferenceResolver: new AgentInferenceResolver(keyVault),
            teamManager: new TeamDefinitionManager(),
            eventStream: new AgentEventStream(),
            ticketStore: ticketStore
        );

        await engine.ExecuteRoleTaskAsync(AgentRole.SecurityEngineer, secTicket);

        mockClient.LastRequest.Should().NotBeNull();
        var userMessage = mockClient.LastRequest!.Messages.First(m => m.Role == "user").Content;

        // Upstream C# code MUST be injected into Security review prompt!
        userMessage.Should().Contain("UPSTREAM INTER-AGENT DELIVERABLE CONTEXT");
        userMessage.Should().Contain("AesGcmVaultService.cs");
        userMessage.Should().Contain("public sealed class AesGcmVaultService");
    }

    [Fact]
    public async Task AgentExecutionEngine_WithDeveloperDeliverables_ShouldInjectCodeIntoQAPrompt()
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

        var engine = new AgentExecutionEngine(
            openRouterClient: mockClient,
            inferenceResolver: new AgentInferenceResolver(keyVault),
            teamManager: new TeamDefinitionManager(),
            eventStream: new AgentEventStream(),
            ticketStore: ticketStore
        );

        await engine.ExecuteRoleTaskAsync(AgentRole.PrincipalQAAnalyst, qaTicket);

        mockClient.LastRequest.Should().NotBeNull();
        var userMessage = mockClient.LastRequest!.Messages.First(m => m.Role == "user").Content;

        // Upstream code and unit tests MUST be injected into QA prompt!
        userMessage.Should().Contain("UPSTREAM INTER-AGENT DELIVERABLE CONTEXT");
        userMessage.Should().Contain("TelemetryService.cs");
        userMessage.Should().Contain("TelemetryServiceTests.cs");
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenNoApiKey_ShouldThrowAndPublishAlert()
    {
        var mockClient = new MockOpenRouterClient();
        var keyVault = new ApiKeyVaultService(); // Empty vault
        var eventStream = new AgentEventStream();

        var engine = new AgentExecutionEngine(
            openRouterClient: mockClient,
            inferenceResolver: new AgentInferenceResolver(keyVault),
            teamManager: new TeamDefinitionManager(),
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

        var act = () => engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, ticket);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*API key*");

        eventStream.GetHistory().Should().Contain(m => m.Type == MessageType.Alert && m.Content.Contains("No active OpenRouter API key"));
    }

    [Fact]
    public async Task GraphWorkflowExecutor_ExecuteTicketAsync_ShouldExecuteSingleTicketAndAdvanceDependencies()
    {
        var ticketStore = new TicketStore();
        var eventStream = new AgentEventStream();
        var handoffRouter = new HandoffRouter(ticketStore, eventStream);
        var mockOpenRouter = new MockOpenRouterClient();
        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: ticketStore);
        var memoryStore = new EmbeddedVectorMemoryStore();
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);
        var decompositionEngine = new WorkDecompositionEngine(ticketStore);

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decompositionEngine,
            handoffRouter,
            executionEngine,
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
        var mockOpenRouter = new MockOpenRouterClient();
        var resolver = new StaticInferenceResolver();
        var executionEngine = new AgentExecutionEngine(mockOpenRouter, resolver, ticketStore: ticketStore);
        var memoryStore = new EmbeddedVectorMemoryStore();
        var consolidationEngine = new MemoryConsolidationEngine(memoryStore);
        var decompositionEngine = new WorkDecompositionEngine(ticketStore);

        var executor = new GraphWorkflowExecutor(
            ticketStore,
            decompositionEngine,
            handoffRouter,
            executionEngine,
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
    public async Task AgentExecutionEngine_WithLeadArchitectADRAndScaffoldCode_ShouldPreserveBothMarkdownADRAndCompanionCodeArtifacts()
    {
        var mockClient = new MockOpenRouterClient();
        // Model outputs an ADR markdown document containing a companion scaffold C# block
        mockClient.ResponseFactory = _ => """
        # ADR-042: High-Performance Order Dispatch Engine
        
        ## Status
        Accepted
        
        ## Decision
        We use bounded Channels for lock-free order sequencing.
        
        ```csharp:IOrderSequencer.cs
        namespace OrderEngine;
        public interface IOrderSequencer
        {
            ValueTask EnqueueOrderAsync(ReadOnlyMemory<byte> orderPayload, CancellationToken ct = default);
        }
        ```
        """;

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);
        var teamManager = new TeamDefinitionManager();
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();

        var engine = new AgentExecutionEngine(
            mockClient,
            resolver,
            teamManager: teamManager,
            eventStream: eventStream
        );

        var archTicket = new TicketItem(
            Id: "TCK-ARCH-001",
            ParentEpicId: "EPIC-001",
            Title: "[Arch] Design Order Dispatch System",
            Description: "ADR design for order dispatch",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["ADR document"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, archTicket);

        // Should produce BOTH the ADR markdown deliverable AND the companion IOrderSequencer.cs
        deliverables.Should().HaveCount(2);

        var adrArtifact = deliverables.FirstOrDefault(d => d.ContentType == "markdown");
        adrArtifact.Should().NotBeNull();
        adrArtifact!.Name.Should().Be("TCK-ARCH-001_ADR.md");
        adrArtifact.Content.Should().Contain("# ADR-042: High-Performance Order Dispatch Engine");

        var codeArtifact = deliverables.FirstOrDefault(d => d.ContentType == "csharp");
        codeArtifact.Should().NotBeNull();
        codeArtifact!.Name.Should().Be("IOrderSequencer.cs");
        codeArtifact.Content.Should().Contain("public interface IOrderSequencer");
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenOpenRouterThrows_ShouldPublishAlertToEventStreamWithDetails()
    {
        var mockClient = new MockOpenRouterClient();
        mockClient.ShouldThrow = true;

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);
        var teamManager = new TeamDefinitionManager();
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();

        var engine = new AgentExecutionEngine(
            mockClient,
            resolver,
            teamManager: teamManager,
            eventStream: eventStream
        );

        var ticket = new TicketItem(
            Id: "TCK-DEV-FAIL",
            ParentEpicId: "EPIC-001",
            Title: "[Dev] Implement Service",
            Description: "Throws exception",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.LeadArchitect,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Code"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        Func<Task> act = async () => await engine.ExecuteRoleTaskAsync(AgentRole.SoftwareDeveloper, ticket);
        await act.Should().ThrowAsync<HttpRequestException>();

        // Verify alert was published to event stream with details
        var alerts = eventStream.GetHistory().Where(m => m.Type == MessageType.Alert).ToList();
        alerts.Should().NotBeEmpty();
        alerts.First().Content.Should().Contain("401 Unauthorized");
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenPrimaryModelProducesEmptyContent_WithConfiguredFallbackModel_ShouldSeamlesslyFailoverToFallbackModel()
    {
        var mockClient = new MockOpenRouterClient();
        mockClient.FullResponseFactory = req =>
        {
            if (req.Model == "deepseek/deepseek-v4-pro-0813")
            {
                // Primary model returns empty content and finish_reason "length"
                return new OpenRouterChatResponse(
                    Id: "gen-empty-primary",
                    Model: req.Model,
                    Choices: [new OpenRouterChoice(0, new OpenRouterMessage("assistant", ""), "length")],
                    Usage: new OpenRouterUsage(100, 4000, 4100)
                );
            }

            // Fallback model returns valid ADR
            return new OpenRouterChatResponse(
                Id: "gen-fallback-ok",
                Model: req.Model,
                Choices: [new OpenRouterChoice(0, new OpenRouterMessage("assistant", "# ADR-014: High-Performance Architecture\n## Status\nAccepted\n## Decision\nUse bounded channels."), "stop")],
                Usage: new OpenRouterUsage(100, 500, 600)
            );
        };

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);

        var archPersona = AgentPersona.CreateDefault(AgentRole.LeadArchitect) with
        {
            DefaultModel = "deepseek/deepseek-v4-pro-0813",
            FallbackModel = "anthropic/claude-3.7-sonnet"
        };
        var teamManager = new TeamDefinitionManager();
        var teamDef = TeamDefinition.CreateDefaultCircusTeam() with
        {
            Members = [new AgentMember(archPersona)]
        };
        teamManager.SetCurrentTeam(teamDef);
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();

        var engine = new AgentExecutionEngine(
            mockClient,
            resolver,
            teamManager: teamManager,
            eventStream: eventStream
        );

        var ticket = new TicketItem(
            Id: "SUB-F23241",
            ParentEpicId: "EPIC-001",
            Title: "Design High-Performance Raft Buffer Architecture",
            Description: "Lead Architect ADR",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["ADR document"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, ticket);

        deliverables.Should().NotBeEmpty();
        deliverables[0].Content.Should().Contain("# ADR-014: High-Performance Architecture");

        // Verify failover event was published
        var events = eventStream.GetHistory().ToList();
        events.Should().Contain(e => e.Content.Contains("Primary model [deepseek/deepseek-v4-pro-0813] produced empty deliverable content") && e.Content.Contains("Initiating autonomous failover to fallback model [anthropic/claude-3.7-sonnet]"));
        events.Should().Contain(e => e.Content.Contains("Fallback model [anthropic/claude-3.7-sonnet] successfully generated"));
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenModelReturnsThinkingTags_ShouldStripThinkingAndPreserveCleanDeliverable()
    {
        var mockClient = new MockOpenRouterClient();
        mockClient.ResponseFactory = _ => """
        <think>
        We are designing the ADR for SUB-F23241.
        Let's consider bounded channels and ValueTask zero allocations.
        </think>
        # ADR-014: High-Performance Clean Architecture
        ## Status
        Accepted
        ## Context
        Production system requiring zero allocations.
        """;

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();
        var engine = new AgentExecutionEngine(mockClient, resolver, eventStream: eventStream);

        var ticket = new TicketItem(
            Id: "SUB-F23241",
            ParentEpicId: "EPIC-001",
            Title: "Design High-Performance Clean Architecture",
            Description: "ADR design",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Clean ADR without thought tags"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, ticket);

        deliverables.Should().NotBeEmpty();
        deliverables[0].Content.Should().NotContain("<think>");
        deliverables[0].Content.Should().NotContain("</think>");
        deliverables[0].Content.Should().Contain("# ADR-014: High-Performance Clean Architecture");
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenModelOutputsDeliverableInsideThinkingTags_ShouldExtractCleanDeliverable()
    {
        var mockClient = new MockOpenRouterClient();
        mockClient.ResponseFactory = _ => """
        <think>
        # ADR-014: Clean Scaffolding Blueprint
        ## Status
        Accepted
        ## Architectural Decision
        Use bounded channels for throughput.
        ```csharp:IPipeline.cs
        namespace Domain;
        public interface IPipeline { }
        ```
        </think>
        """;

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();
        var engine = new AgentExecutionEngine(mockClient, resolver, eventStream: eventStream);

        var ticket = new TicketItem(
            Id: "SUB-F23241",
            ParentEpicId: "EPIC-001",
            Title: "Scaffold Blueprint",
            Description: "Scaffold design",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Scaffold deliverable"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, ticket);

        deliverables.Should().NotBeEmpty();
        deliverables.Should().Contain(d => d.ContentType == "markdown" && d.Content.Contains("# ADR-014: Clean Scaffolding Blueprint"));
        deliverables.Should().Contain(d => d.ContentType == "csharp" && d.Name == "IPipeline.cs");
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenReasoningContentFieldPopulatedAndContentEmpty_ShouldExtractFromReasoning()
    {
        var mockClient = new MockOpenRouterClient();
        mockClient.FullResponseFactory = req => new OpenRouterChatResponse(
            Id: "gen-reasoning-only",
            Model: req.Model,
            Choices: [
                new OpenRouterChoice(
                    Index: 0,
                    Message: new OpenRouterMessage("assistant", Content: "", Reasoning: "# ADR-014: High-Performance Architecture\n## Status\nAccepted\n## Decision\nBounded channels."),
                    FinishReason: "stop"
                )
            ]
        );

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();
        var engine = new AgentExecutionEngine(mockClient, resolver, eventStream: eventStream);

        var ticket = new TicketItem(
            Id: "SUB-F23241",
            ParentEpicId: "EPIC-001",
            Title: "Design Architecture",
            Description: "Reasoning model test",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["ADR document"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, ticket);

        deliverables.Should().NotBeEmpty();
        deliverables[0].Content.Should().Contain("# ADR-014: High-Performance Architecture");
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenBothPrimaryAndFallbackFail_ShouldThrowDescriptiveException()
    {
        var mockClient = new MockOpenRouterClient();
        mockClient.FullResponseFactory = req => new OpenRouterChatResponse(
            Id: "gen-empty-both",
            Model: req.Model,
            Choices: [new OpenRouterChoice(0, new OpenRouterMessage("assistant", ""), "length")]
        );

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);

        var archPersona = AgentPersona.CreateDefault(AgentRole.LeadArchitect) with
        {
            DefaultModel = "deepseek/deepseek-v4-pro-0813",
            FallbackModel = "anthropic/claude-3.7-sonnet"
        };
        var teamManager = new TeamDefinitionManager();
        var teamDef = TeamDefinition.CreateDefaultCircusTeam() with
        {
            Members = [new AgentMember(archPersona)]
        };
        teamManager.SetCurrentTeam(teamDef);
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();

        var engine = new AgentExecutionEngine(
            mockClient,
            resolver,
            teamManager: teamManager,
            eventStream: eventStream
        );

        var ticket = new TicketItem(
            Id: "SUB-F23241",
            ParentEpicId: "EPIC-001",
            Title: "Design Architecture",
            Description: "ADR test",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["ADR"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var act = () => engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, ticket);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Both primary model [deepseek/deepseek-v4-pro-0813]*and fallback model [anthropic/claude-3.7-sonnet]*");
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenPrimaryModelTimesOut_WithFallbackConfigured_ShouldFailoverToFallbackModel()
    {
        var mockClient = new MockOpenRouterClient();
        mockClient.FullResponseFactory = req =>
        {
            if (req.Model.Contains("deepseek"))
            {
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
            }

            return new OpenRouterChatResponse(
                Id: "gen-fallback-success",
                Model: req.Model,
                Choices: [
                    new OpenRouterChoice(
                        0,
                        new OpenRouterMessage("assistant", "# ADR-015: Fallback Architecture\n## Status\nAccepted\n## Decision\nHigh resiliency architecture."),
                        "stop"
                    )
                ]
            );
        };

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);

        var archPersona = AgentPersona.CreateDefault(AgentRole.LeadArchitect) with
        {
            DefaultModel = "deepseek/deepseek-r1",
            FallbackModel = "anthropic/claude-3.7-sonnet"
        };
        var teamManager = new TeamDefinitionManager();
        var teamDef = TeamDefinition.CreateDefaultCircusTeam() with
        {
            Members = [new AgentMember(archPersona)]
        };
        teamManager.SetCurrentTeam(teamDef);
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();

        var engine = new AgentExecutionEngine(
            mockClient,
            resolver,
            teamManager: teamManager,
            eventStream: eventStream
        );

        var ticket = new TicketItem(
            Id: "SUB-F23241",
            ParentEpicId: "EPIC-001",
            Title: "Design Resilient Architecture",
            Description: "ADR timeout failover test",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["ADR"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, ticket);

        deliverables.Should().NotBeEmpty();
        deliverables[0].Content.Should().Contain("# ADR-015: Fallback Architecture");
        eventStream.GetHistory().Should().Contain(m => m.Content.Contains("Initiating autonomous failover to fallback model [anthropic/claude-3.7-sonnet]"));
    }

    [Fact]
    public async Task AgentExecutionEngine_WhenCallerCancelsToken_ShouldNotAttemptFallback()
    {
        var mockClient = new MockOpenRouterClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);

        var archPersona = AgentPersona.CreateDefault(AgentRole.LeadArchitect) with
        {
            DefaultModel = "deepseek/deepseek-r1",
            FallbackModel = "anthropic/claude-3.7-sonnet"
        };
        var teamManager = new TeamDefinitionManager();
        var teamDef = TeamDefinition.CreateDefaultCircusTeam() with
        {
            Members = [new AgentMember(archPersona)]
        };
        teamManager.SetCurrentTeam(teamDef);
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();

        var engine = new AgentExecutionEngine(
            mockClient,
            resolver,
            teamManager: teamManager,
            eventStream: eventStream
        );

        var ticket = new TicketItem(
            Id: "SUB-F23241",
            ParentEpicId: "EPIC-001",
            Title: "Design Resilient Architecture",
            Description: "Cancellation test",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["ADR"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var act = () => engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, ticket, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        eventStream.GetHistory().Should().NotContain(m => m.Content.Contains("Initiating autonomous failover"));
    }

    [Fact]
    public async Task AgentExecutionEngine_WithLeadArchitectCommentTaggedCode_ShouldExtractCompanionScaffolds()
    {
        var mockClient = new MockOpenRouterClient();
        mockClient.ResponseFactory = _ => """
        # ADR-014: High-Performance Order Saga
        
        ## Status
        Accepted
        
        ## Decision
        Use state machine.
        
        ```csharp
        // File: Contracts/IOrderSagaService.cs
        namespace CarnotCycleCircus.Core.Domain.OrderSaga;
        public interface IOrderSagaService { }
        ```
        
        ```csharp
        // File: Models/OrderSagaModels.cs
        namespace CarnotCycleCircus.Core.Domain.OrderSaga;
        public readonly record struct OrderSagaResult(string Id, bool IsSuccess);
        ```
        """;

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();
        var engine = new AgentExecutionEngine(mockClient, resolver, eventStream: eventStream);

        var archTicket = new TicketItem(
            Id: "SUB-ARCH-01",
            ParentEpicId: "EPIC-01",
            Title: "[Arch] Design ADR & Scaffold Clean Architecture for Distributed Order Saga",
            Description: "ADR and scaffold",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Clean Architecture scaffold"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, archTicket);

        deliverables.Should().HaveCount(3);
        deliverables.Should().Contain(d => d.ContentType == "markdown" && d.Name == "SUB-ARCH-01_ADR.md");
        deliverables.Should().Contain(d => d.ContentType == "csharp" && d.Name == "IOrderSagaService.cs");
        deliverables.Should().Contain(d => d.ContentType == "csharp" && d.Name == "OrderSagaModels.cs");
    }

    [Fact]
    public async Task AgentExecutionEngine_WithLeadArchitectMarkdownOnly_ShouldSynthesizeFallbackCleanScaffolds()
    {
        var mockClient = new MockOpenRouterClient();
        mockClient.ResponseFactory = _ => """
        # ADR-014: Realtime Telemetry Architecture
        
        ## Status
        Accepted
        
        ## Decision
        Use zero allocation channels for telemetry ingestion.
        """;

        var keyVault = new ApiKeyVaultService();
        keyVault.AddOrUpdateKey("Test Key", "sk-or-v1-validkey12345678901234567890", isActive: true);
        var resolver = new AgentInferenceResolver(keyVault);
        var eventStream = new AgentEventStream();
        var engine = new AgentExecutionEngine(mockClient, resolver, eventStream: eventStream);

        var archTicket = new TicketItem(
            Id: "SUB-ARCH-02",
            ParentEpicId: "EPIC-02",
            Title: "[Arch] Design ADR & Scaffold Clean Architecture for Real-time Telemetry Pipeline: Core Engine & Protocols",
            Description: "ADR and scaffold",
            Type: TicketType.Subtask,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.LeadArchitect,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.High,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Clean Architecture scaffold"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        var deliverables = await engine.ExecuteRoleTaskAsync(AgentRole.LeadArchitect, archTicket);

        deliverables.Should().Contain(d => d.ContentType == "markdown" && d.Name == "SUB-ARCH-02_ADR.md");
        deliverables.Should().Contain(d => d.ContentType == "csharp" && d.Name == "IRealTimeTelemetryPipelineService.cs");
        deliverables.Should().Contain(d => d.ContentType == "csharp" && d.Name == "RealTimeTelemetryPipelineModels.cs");
        deliverables.Should().Contain(d => d.ContentType == "csharp" && d.Name == "RealTimeTelemetryPipelineServiceCollectionExtensions.cs");
    }
}
