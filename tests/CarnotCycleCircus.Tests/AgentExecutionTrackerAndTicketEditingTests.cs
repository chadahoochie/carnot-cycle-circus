using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Security;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class AgentExecutionTrackerAndTicketEditingTests
{
    [Fact]
    public void TicketItem_EditingAndRenaming_ShouldPerformNonDestructiveMutations()
    {
        var original = new TicketItem(
            Id: "TCK-EDIT-01",
            ParentEpicId: null,
            Title: "Initial Title",
            Description: "Initial Description",
            Type: TicketType.Feature,
            Status: TicketStatus.Ready,
            AssigneeRole: AgentRole.SoftwareDeveloper,
            CreatedByRole: AgentRole.TechnicalProductManager,
            Priority: TicketPriority.Low,
            DependsOnTicketIds: Array.Empty<string>(),
            AcceptanceCriteria: ["Old Criterion 1"],
            Deliverables: Array.Empty<ArtifactItem>(),
            Metadata: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UtcNow
        );

        // 1. Rename title
        var renamed = original.WithTitle("Renamed Circus Ticket");
        renamed.Title.Should().Be("Renamed Circus Ticket");
        original.Title.Should().Be("Initial Title"); // Immutability verification

        // 2. Update description
        var descUpdated = renamed.WithDescription("Updated Detailed Description");
        descUpdated.Description.Should().Be("Updated Detailed Description");
        renamed.Description.Should().Be("Initial Description");

        // 3. Update priority
        var critUpdated = descUpdated.WithPriority(TicketPriority.Critical);
        critUpdated.Priority.Should().Be(TicketPriority.Critical);

        // 4. Update acceptance criteria
        var acUpdated = critUpdated.WithAcceptanceCriteria(["New Criterion A", "New Criterion B"]);
        acUpdated.AcceptanceCriteria.Should().HaveCount(2);
        acUpdated.AcceptanceCriteria.Should().Contain("New Criterion A");

        // 5. Atomic UpdateDetails
        var bulkUpdated = original.UpdateDetails(
            title: "Bulk Updated Title",
            description: "Bulk Description",
            priority: TicketPriority.High,
            criteria: ["Bulk 1", "Bulk 2", "Bulk 3"]
        );

        bulkUpdated.Title.Should().Be("Bulk Updated Title");
        bulkUpdated.Description.Should().Be("Bulk Description");
        bulkUpdated.Priority.Should().Be(TicketPriority.High);
        bulkUpdated.AcceptanceCriteria.Should().HaveCount(3);
        bulkUpdated.Id.Should().Be(original.Id);
    }

    [Fact]
    public void AgentExecutionTracker_Lifecycle_ShouldTrackChunksAndFailovers()
    {
        var tracker = new AgentExecutionTracker();
        AgentExecutionTrace? lastNotified = null;
        int notificationCount = 0;
        tracker.OnExecutionUpdated += trace =>
        {
            notificationCount++;
            if (trace != null)
            {
                lastNotified = trace;
            }
        };

        // 1. Start execution
        tracker.StartExecution(
            role: AgentRole.SoftwareDeveloper,
            roleName: "Devon Coder",
            ticketId: "TCK-100",
            ticketTitle: "Implement API Client",
            primaryModel: "primary/fast-coder",
            fallbackModel: "fallback/backup-coder",
            systemPrompt: "You are an elite C# developer.",
            userPrompt: "Generate the implementation.",
            upstreamDeliverables: ["ArchitectureADR.md", "SpecificationPRD.md"]
        );

        tracker.CurrentExecution.Should().NotBeNull();
        tracker.CurrentExecution!.TicketId.Should().Be("TCK-100");
        tracker.CurrentExecution.PrimaryModel.Should().Be("primary/fast-coder");
        tracker.CurrentExecution.FallbackModel.Should().Be("fallback/backup-coder");
        tracker.CurrentExecution.ActiveModel.Should().Be("primary/fast-coder");
        tracker.CurrentExecution.IsFallbackActive.Should().BeFalse();
        tracker.CurrentExecution.IsRunning.Should().BeTrue();
        tracker.CurrentExecution.SystemPrompt.Should().Contain("elite C# developer");
        tracker.CurrentExecution.UpstreamDeliverableNames.Should().HaveCount(2);

        // 2. Append chunks
        tracker.AppendChunk("TCK-100", "public class ");
        tracker.AppendChunk("TCK-100", "ApiClient { }");

        tracker.CurrentExecution.ChunksReceived.Should().Be(2);
        tracker.CurrentExecution.FullStreamOutput.Should().Be("public class ApiClient { }");
        tracker.CurrentExecution.LiveSnippet.Should().Be("public class ApiClient { }");

        // 3. Record failover
        tracker.RecordFailover("TCK-100", "fallback/backup-coder", "HTTP 429: Rate limit exceeded");

        tracker.CurrentExecution.IsFallbackActive.Should().BeTrue();
        tracker.CurrentExecution.ActiveModel.Should().Be("fallback/backup-coder");
        tracker.CurrentExecution.FailoverReason.Should().Be("HTTP 429: Rate limit exceeded");
        tracker.CurrentExecution.CurrentPhase.Should().ContainEquivalentOf("autonomous failover");

        // 4. Append more chunks on fallback
        tracker.AppendChunk("TCK-100", "\n// Generated via fallback");
        tracker.CurrentExecution.ChunksReceived.Should().Be(3);
        tracker.CurrentExecution.FullStreamOutput.Should().Contain("// Generated via fallback");

        // 5. Complete execution
        tracker.CompleteExecution("TCK-100", success: true);

        tracker.CurrentExecution.Should().BeNull();
        tracker.LastExecution.Should().NotBeNull();
        tracker.LastExecution!.TicketId.Should().Be("TCK-100");
        tracker.LastExecution.IsSuccess.Should().BeTrue();
        tracker.LastExecution.IsRunning.Should().BeFalse();
        tracker.LastExecution.CompletedAt.Should().NotBeNull();

        // 6. Trace query by ticket ID
        var ticketTrace = tracker.GetExecutionForTicket("TCK-100");
        ticketTrace.Should().NotBeNull();
        ticketTrace!.TicketTitle.Should().Be("Implement API Client");
        ticketTrace.IsFallbackActive.Should().BeTrue();

        notificationCount.Should().BeGreaterThan(0);
        lastNotified.Should().NotBeNull();
    }

    [Fact]
    public void AgentInferenceResolver_FallbackModelResolution_ShouldResolveCorrectly()
    {
        var keyVault = new ApiKeyVaultService();
        var key = keyVault.AddOrUpdateKey("Global Key", "sk-global-secret-key-12345", isActive: true);

        var resolver = new AgentInferenceResolver(keyVault);
        var team = EngineeringTeam.CreateDefault() with
        {
            DefaultFallbackModel = "squad/default-fallback-model",
            ActiveGlobalApiKeyId = key.KeyId
        };

        // Case A: Agent member with explicit fallback model
        var memberWithFallback = new AgentMember(
            Persona: AgentPersona.CreateDefault(AgentRole.SoftwareDeveloper) with
            {
                DefaultModel = "primary/coder-99b",
                FallbackModel = "member/custom-fallback"
            }
        );

        var configA = resolver.ResolveInferenceConfig(memberWithFallback, team);
        configA.PrimaryModel.Should().Be("primary/coder-99b");
        configA.FallbackModel.Should().Be("member/custom-fallback");
        configA.ApiKey.Should().Be("sk-global-secret-key-12345");

        // Case B: Agent member without explicit fallback model should inherit team default fallback
        var memberNoFallback = new AgentMember(
            Persona: AgentPersona.CreateDefault(AgentRole.TechnicalProductManager) with
            {
                DefaultModel = "primary/tpm-model",
                FallbackModel = ""
            }
        );

        var configB = resolver.ResolveInferenceConfig(memberNoFallback, team);
        configB.PrimaryModel.Should().Be("primary/tpm-model");
        configB.FallbackModel.Should().Be("squad/default-fallback-model");

        // Case C: When fallback model is identical to primary model, fallback should be null
        var memberDuplicateFallback = new AgentMember(
            Persona: AgentPersona.CreateDefault(AgentRole.LeadArchitect) with
            {
                DefaultModel = "primary/arch-model",
                FallbackModel = "primary/arch-model"
            }
        );
        var teamDuplicate = team with { DefaultFallbackModel = "primary/arch-model" };

        var configC = resolver.ResolveInferenceConfig(memberDuplicateFallback, teamDuplicate);
        configC.PrimaryModel.Should().Be("primary/arch-model");
        configC.FallbackModel.Should().BeNull();
    }
}
