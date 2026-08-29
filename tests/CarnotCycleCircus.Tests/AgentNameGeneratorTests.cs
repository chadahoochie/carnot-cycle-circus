using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Skills;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class AgentNameGeneratorTests
{
    private readonly IAgentNameGenerator _generator = new AgentNameGenerator();

    [Theory]
    [InlineData(AgentRole.SoftwareDeveloper, "Modern C# 13 & Zero-Allocation Dogma", "Zero-Alloc")]
    [InlineData(AgentRole.SecurityEngineer, "STRIDE Threat Modeling (Paranoid Mode)", "STRIDE")]
    [InlineData(AgentRole.OptimizationEngineer, "Nanosecond Obsession & Zero Allocations", "Nanosecond")]
    [InlineData(AgentRole.TechnicalProductManager, "Jira Juggling & Buzzword Mastery", "Buzzword")]
    [InlineData(AgentRole.PrincipalQAAnalyst, "Demonic Edge-Case Crafting", "Demonic")]
    public void GenerateSuggestedName_WithRoleAndSkills_ShouldIncorporateAbsurdThemeAndSkillKeywords(
        AgentRole role,
        string skillName,
        string expectedSubstring)
    {
        var skill = new SkillDefinition(
            Id: $"skill-{role.ToString().ToLowerInvariant()}",
            Name: skillName,
            Description: "Specialized circus capability",
            Instructions: "Execute with zero heap allocations.",
            RecommendedTools: ["csharp_syntax_check"]
        );

        var suggestions = _generator.GenerateNameSuggestions(role, [skill], count: 5, seed: 42);

        suggestions.Should().NotBeEmpty();
        suggestions.Should().HaveCount(5);

        // At least one suggestion should reflect the skill's theme or keywords
        var hasSkillMention = suggestions.Any(s =>
            s.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Span", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Zero", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Tinfoil", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Chaos", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Velocity", StringComparison.OrdinalIgnoreCase));

        hasSkillMention.Should().BeTrue();
    }

    [Theory]
    [InlineData(AgentRole.RequirementsResearcher, "Requirements Researcher")]
    [InlineData(AgentRole.TechnicalProductManager, "TPM")]
    [InlineData(AgentRole.LeadArchitect, "Lead Architect")]
    [InlineData(AgentRole.SoftwareDeveloper, "Senior Developer")]
    [InlineData(AgentRole.SecurityEngineer, "Security Engineer")]
    [InlineData(AgentRole.OptimizationEngineer, "Optimization Engineer")]
    [InlineData(AgentRole.PrincipalQAAnalyst, "Principal QA Analyst")]
    [InlineData(AgentRole.IntegrationEngineer, "Release Integrator")]
    public void GenerateSuggestedName_WithoutSkills_ShouldProvideRoleBasedAbsurdName(AgentRole role, string expectedRoleSuffix)
    {
        var suggestedName = _generator.GenerateSuggestedName(role, skills: null, seed: 100);

        suggestedName.Should().NotBeNullOrWhiteSpace();
        suggestedName.Should().Contain(expectedRoleSuffix);
    }

    [Fact]
    public void GenerateNameSuggestions_ShouldReturnUniqueVariedSuggestions()
    {
        var role = AgentRole.SoftwareDeveloper;
        var skills = new List<SkillDefinition>
        {
            new("skill-csharp", "Modern C# 13 & Zero-Allocation Dogma", "Zero-alloc patterns", "Use Span/Memory", ["csharp_syntax_check"]),
            new("skill-perf", "Nanosecond Obsession & Zero Allocations", "P99 latency profiling", "Measure Gen0", ["test_runner"])
        };

        var suggestions = _generator.GenerateNameSuggestions(role, skills, count: 6, seed: 777);

        suggestions.Should().HaveCount(6);
        suggestions.Distinct().Should().HaveCount(6, "all generated suggestions should be unique");
    }

    [Fact]
    public void GenerateSuggestedName_WithCustomSkill_ShouldExtractSkillConcepts()
    {
        var customSkill = new SkillDefinition(
            Id: "skill-graphql-trapeze",
            Name: "GraphQL Schema Federation & Resolver Architecture",
            Description: "Federated GraphQL schemas with distributed query planning.",
            Instructions: "Validate subgraphs and minimize N+1 resolver queries.",
            RecommendedTools: ["web_search"]
        );

        var suggestions = _generator.GenerateNameSuggestions(AgentRole.LeadArchitect, [customSkill], count: 4, seed: 123);

        suggestions.Should().NotBeEmpty();
        suggestions.Any(s => s.Contains("GraphQL", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public void GenerateSystemPrompt_ShouldEnforceDeliverableIsolationContract_AndContainSkillDirectives()
    {
        var role = AgentRole.SoftwareDeveloper;
        var skills = new List<SkillDefinition>
        {
            new("skill-csharp", "Modern C# 13 & Zero-Allocation Dogma", "Zero alloc", "Ban all setters and use ReadOnlySpan.", ["csharp_syntax_check"])
        };

        var prompt = _generator.GenerateSystemPrompt(role, "Devon 'Zero-Alloc' Crashdump (Senior Developer)", skills);

        prompt.Should().NotBeNullOrWhiteSpace();
        prompt.Should().Contain("Devon 'Zero-Alloc' Crashdump (Senior Developer)");
        prompt.Should().Contain("DELIVERABLE ISOLATION CONTRACT");
        prompt.Should().Contain("Ban all setters and use ReadOnlySpan.");
        prompt.Should().Contain("strictly professional");
    }

    [Fact]
    public void SeedStability_ShouldBeDeterministicWithGivenSeed()
    {
        var role = AgentRole.SecurityEngineer;
        var skills = new List<SkillDefinition>
        {
            new("skill-stride", "STRIDE Threat Modeling (Paranoid Mode)", "Vulnerability analysis", "Check trust boundaries", ["web_search"])
        };

        var run1 = _generator.GenerateNameSuggestions(role, skills, count: 4, seed: 9999);
        var run2 = _generator.GenerateNameSuggestions(role, skills, count: 4, seed: 9999);

        run1.Should().BeEquivalentTo(run2);
    }
}
