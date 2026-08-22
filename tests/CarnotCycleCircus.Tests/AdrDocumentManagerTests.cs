using CarnotCycleCircus.Core.Domain.Docs;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class AdrDocumentManagerTests
{
    private readonly AdrDocumentManager _manager = new();

    [Fact]
    public void SaveAdr_ShouldStoreAndFormatMarkdown()
    {
        var adr = new ArchitecturalDecisionRecord(
            Id: "ADR-100",
            Title: "Test Decision",
            Status: AdrStatus.Accepted,
            Context: "Need unit test for ADRs",
            Decision: "Implement ADR manager",
            AlternativesConsidered: ["No docs"],
            ConsequencesPositive: ["Clear traceability"],
            ConsequencesNegative: ["Small authoring overhead"],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );

        _manager.SaveAdr(adr);
        var retrieved = _manager.GetAdr("ADR-100");

        retrieved.Should().NotBeNull();
        retrieved!.ToMarkdown().Should().Contain("# ADR-100: Test Decision");
        retrieved.ToMarkdown().Should().Contain("Clear traceability");
    }

    [Fact]
    public void ExportCompleteMarkdownBundle_ShouldContainAdrsAndDocs()
    {
        var bundle = _manager.ExportCompleteMarkdownBundle();

        bundle.Should().Contain("Project Documentation Bundle");
        bundle.Should().Contain("Architectural Decision Records");
        bundle.Should().Contain("ADR-001");
        bundle.Should().Contain("C4 System Architecture Model");
    }
}
