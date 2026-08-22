using CarnotCycleCircus.Core.Domain.Agents;
using CarnotCycleCircus.Core.Domain.Memory;
using CarnotCycleCircus.Core.Domain.Tools;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class ToolSandboxTests
{
    [Fact]
    public async Task WebSearchTool_ShouldReturnTechnicalResults()
    {
        var tool = new WebSearchTool();
        var context = new ToolExecutionContext(
            ToolName: "web_search",
            Arguments: new Dictionary<string, string> { ["query"] = "STRIDE security model" },
            InvokingRole: AgentRole.SecurityEngineer
        );

        var result = await tool.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("STRIDE");
    }

    [Fact]
    public async Task CSharpSyntaxCheckTool_ShouldPassOnValidCode_AndFailOnUnbalancedBraces()
    {
        var tool = new CSharpSyntaxCheckTool();
        
        var validContext = new ToolExecutionContext(
            ToolName: "csharp_syntax_check",
            Arguments: new Dictionary<string, string> { ["code"] = "public record MyRecord(string Name);" },
            InvokingRole: AgentRole.SoftwareDeveloper
        );
        var validResult = await tool.ExecuteAsync(validContext);
        validResult.Success.Should().BeTrue();

        var brokenContext = new ToolExecutionContext(
            ToolName: "csharp_syntax_check",
            Arguments: new Dictionary<string, string> { ["code"] = "public class Foo { void Bar() { " },
            InvokingRole: AgentRole.SoftwareDeveloper
        );
        var brokenResult = await tool.ExecuteAsync(brokenContext);
        brokenResult.Success.Should().BeFalse();
        brokenResult.ErrorMessage.Should().Contain("Unclosed open brace");
    }

    [Fact]
    public async Task TestRunnerTool_ShouldExecuteAndReportCoverage()
    {
        var tool = new TestRunnerTool();
        var context = new ToolExecutionContext(
            ToolName: "test_runner",
            Arguments: new Dictionary<string, string> { ["testSuite"] = "UnitTests" },
            InvokingRole: AgentRole.PrincipalQAAnalyst
        );

        var result = await tool.ExecuteAsync(context);
        result.Success.Should().BeTrue();
        result.Metadata.Should().ContainKey("Coverage");
    }

    [Fact]
    public async Task AdrWriterTool_ShouldGenerateFormattedMarkdown()
    {
        var tool = new AdrWriterTool();
        var context = new ToolExecutionContext(
            ToolName: "adr_writer",
            Arguments: new Dictionary<string, string>
            {
                ["title"] = "Use Embedded Vector Store",
                ["context"] = "Need offline memory capabilities",
                ["decision"] = "Implement EmbeddedVectorMemoryStore",
                ["consequences"] = "Zero external dependencies"
            },
            InvokingRole: AgentRole.LeadArchitect
        );

        var result = await tool.ExecuteAsync(context);
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("# ADR-");
        result.Output.Should().Contain("Use Embedded Vector Store");
    }

    [Fact]
    public async Task MemoryLookupTool_ShouldFindMatchingMemories()
    {
        var memStore = new EmbeddedVectorMemoryStore();
        await memStore.StoreAsync(new MemoryEntry(
            Id: "M1",
            Type: MemoryType.Semantic,
            Role: AgentRole.LeadArchitect,
            Content: "Always prefer ValueTask pipelines for low latency",
            Embedding: Array.Empty<float>(),
            Importance: 0.9f,
            Tags: new Dictionary<string, string>(),
            Timestamp: DateTimeOffset.UtcNow,
            LastAccessedAt: DateTimeOffset.UtcNow
        ));

        var tool = new MemoryLookupTool(memStore);
        var context = new ToolExecutionContext(
            ToolName: "memory_lookup",
            Arguments: new Dictionary<string, string> { ["query"] = "ValueTask pipelines" },
            InvokingRole: AgentRole.SoftwareDeveloper
        );

        var result = await tool.ExecuteAsync(context);
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("ValueTask pipelines");
    }
}
