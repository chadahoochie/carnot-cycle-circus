using CarnotCycleCircus.Core.Domain.Knowledge;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class KnowledgeMapTests
{
    private readonly KnowledgeMapService _service = new();

    [Fact]
    public void AddNodeAndEdge_ShouldBeReflectedInFullMap()
    {
        var node = new KnowledgeNode(
            Id: "KN-999",
            Label: "Actor Pattern Hierarchy",
            Category: "Pattern",
            Summary: "Actors handle concurrent state sequentially.",
            Attributes: new Dictionary<string, string> { ["Type"] = "Concurrency" }
        );

        _service.AddOrUpdateNode(node);
        _service.AddEdge("KN-999", "KN-001", "Implements");

        var map = _service.GetFullMap();
        map.Nodes.Should().Contain(n => n.Id == "KN-999");
        map.Edges.Should().Contain(e => e.SourceNodeId == "KN-999" && e.TargetNodeId == "KN-001");
    }

    [Fact]
    public void ExtractSubGraphContext_ShouldReturnFormattedContextBlock()
    {
        var context = _service.ExtractSubGraphContext("zero allocation");

        context.Should().Contain("[AI Knowledge Map Context]");
        context.Should().Contain("Zero-Allocation ValueTask Pipelines");
    }
}
