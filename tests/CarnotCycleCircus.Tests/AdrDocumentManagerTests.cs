using CarnotCycleCircus.Core.Domain.Docs;
using Shouldly;
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

        retrieved.ShouldNotBeNull();
        retrieved!.ToMarkdown().ShouldContain("# ADR-100: Test Decision");
        retrieved.ToMarkdown().ShouldContain("Clear traceability");
    }

    [Fact]
    public void ExportCompleteMarkdownBundle_ShouldContainAdrsAndDocs()
    {
        var bundle = _manager.ExportCompleteMarkdownBundle();

        bundle.ShouldContain("Project Documentation Bundle");
        bundle.ShouldContain("Architectural Decision Records");
        bundle.ShouldContain("ADR-001");
        bundle.ShouldContain("C4 System Architecture Model");
    }
}
