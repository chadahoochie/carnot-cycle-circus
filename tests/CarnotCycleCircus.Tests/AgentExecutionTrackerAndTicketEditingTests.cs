using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Events;
using CarnotCycleCircus.Core.Domain.Inference;
using CarnotCycleCircus.Core.Domain.Security;
using CarnotCycleCircus.Core.Domain.Teams;
using CarnotCycleCircus.Core.Domain.Tickets;
using Shouldly;
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
        renamed.Title.ShouldBe("Renamed Circus Ticket");
        original.Title.ShouldBe("Initial Title"); // Immutability verification

        // 2. Update description
        var descUpdated = renamed.WithDescription("Updated Detailed Description");
        descUpdated.Description.ShouldBe("Updated Detailed Description");
        renamed.Description.ShouldBe("Initial Description");

        // 3. Update priority
        var critUpdated = descUpdated.WithPriority(TicketPriority.Critical);
        critUpdated.Priority.ShouldBe(TicketPriority.Critical);

        // 4. Update acceptance criteria
        var acUpdated = critUpdated.WithAcceptanceCriteria(["New Criterion A", "New Criterion B"]);
        acUpdated.AcceptanceCriteria.Count().ShouldBe(2);
        acUpdated.AcceptanceCriteria.ShouldContain("New Criterion A");

        // 5. Atomic UpdateDetails
        var bulkUpdated = original.UpdateDetails(
            title: "Bulk Updated Title",
            description: "Bulk Description",
            priority: TicketPriority.High,
            criteria: ["Bulk 1", "Bulk 2", "Bulk 3"]
        );

        bulkUpdated.Title.ShouldBe("Bulk Updated Title");
        bulkUpdated.Description.ShouldBe("Bulk Description");
        bulkUpdated.Priority.ShouldBe(TicketPriority.High);
        bulkUpdated.AcceptanceCriteria.Count().ShouldBe(3);
        bulkUpdated.Id.ShouldBe(original.Id);
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

        tracker.CurrentExecution.ShouldNotBeNull();
        tracker.CurrentExecution!.TicketId.ShouldBe("TCK-100");
        tracker.CurrentExecution.PrimaryModel.ShouldBe("primary/fast-coder");
        tracker.CurrentExecution.FallbackModel.ShouldBe("fallback/backup-coder");
        tracker.CurrentExecution.ActiveModel.ShouldBe("primary/fast-coder");
        tracker.CurrentExecution.IsFallbackActive.ShouldBeFalse();
        tracker.CurrentExecution.IsRunning.ShouldBeTrue();
        tracker.CurrentExecution.SystemPrompt.ShouldNotBeNull();
        tracker.CurrentExecution.SystemPrompt.ShouldContain("elite C# developer");
        tracker.CurrentExecution.UpstreamDeliverableNames.Count().ShouldBe(2);

        // 2. Append chunks
        tracker.AppendChunk("TCK-100", "public class ");
        tracker.AppendChunk("TCK-100", "ApiClient { }");

        tracker.CurrentExecution.ChunksReceived.ShouldBe(2);
        tracker.CurrentExecution.FullStreamOutput.ShouldBe("public class ApiClient { }");
        tracker.CurrentExecution.LiveSnippet.ShouldBe("public class ApiClient { }");

        // 3. Record failover
        tracker.RecordFailover("TCK-100", "fallback/backup-coder", "HTTP 429: Rate limit exceeded");

        tracker.CurrentExecution.IsFallbackActive.ShouldBeTrue();
        tracker.CurrentExecution.ActiveModel.ShouldBe("fallback/backup-coder");
        tracker.CurrentExecution.FailoverReason.ShouldBe("HTTP 429: Rate limit exceeded");
        tracker.CurrentExecution.CurrentPhase.ShouldContain("autonomous failover", Case.Insensitive);

        // 4. Append more chunks on fallback
        tracker.AppendChunk("TCK-100", "\n// Generated via fallback");
        tracker.CurrentExecution.ChunksReceived.ShouldBe(3);
        tracker.CurrentExecution.FullStreamOutput.ShouldContain("// Generated via fallback");

        // 5. Complete execution
        tracker.CompleteExecution("TCK-100", success: true);

        tracker.CurrentExecution.ShouldBeNull();
        tracker.LastExecution.ShouldNotBeNull();
        tracker.LastExecution!.TicketId.ShouldBe("TCK-100");
        tracker.LastExecution.IsSuccess.ShouldBeTrue();
        tracker.LastExecution.IsRunning.ShouldBeFalse();
        tracker.LastExecution.CompletedAt.ShouldNotBeNull();

        // 6. Trace query by ticket ID
        var ticketTrace = tracker.GetExecutionForTicket("TCK-100");
        ticketTrace.ShouldNotBeNull();
        ticketTrace!.TicketTitle.ShouldBe("Implement API Client");
        ticketTrace.IsFallbackActive.ShouldBeTrue();

        notificationCount.ShouldBeGreaterThan(0);
        lastNotified.ShouldNotBeNull();
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
        configA.PrimaryModel.ShouldBe("primary/coder-99b");
        configA.FallbackModel.ShouldBe("member/custom-fallback");
        configA.ApiKey.ShouldBe("sk-global-secret-key-12345");

        // Case B: Agent member without explicit fallback model should inherit team default fallback
        var memberNoFallback = new AgentMember(
            Persona: AgentPersona.CreateDefault(AgentRole.TechnicalProductManager) with
            {
                DefaultModel = "primary/tpm-model",
                FallbackModel = ""
            }
        );

        var configB = resolver.ResolveInferenceConfig(memberNoFallback, team);
        configB.PrimaryModel.ShouldBe("primary/tpm-model");
        configB.FallbackModel.ShouldBe("squad/default-fallback-model");

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
        configC.PrimaryModel.ShouldBe("primary/arch-model");
        configC.FallbackModel.ShouldBeNull();
    }
}
