using CarnotCycleCircus.Core.Domain.Tickets;

namespace CarnotCycleCircus.Core.Domain.Standards;

public record EngineeringStandardsProfile(
    string Name,
    double MinimumCodeCoveragePercent = 80.0,
    bool RequireUnitTestsForFeatures = true,
    bool RequireRcaForBugs = true,
    bool RequireRegressionTestForBugs = true,
    bool RequireAdrForEpics = true,
    bool RequireStrideSecurityReview = true,
    bool RequireZeroAllocationAudit = true
)
{
    public static EngineeringStandardsProfile Default => new("🚨 The Fun Police (Zero-Tolerance Quality Gates)");
}

public record ValidationResult(
    bool IsValid,
    IReadOnlyList<string> Violations
)
{
    public static ValidationResult Success() => new(true, Array.Empty<string>());
    public static ValidationResult Failure(params string[] violations) => new(false, violations);
}

public interface IStandardsValidator
{
    EngineeringStandardsProfile CurrentProfile { get; set; }
    ValidationResult ValidateTicketForCompletion(TicketItem ticket);
}

public class StandardsValidator : IStandardsValidator
{
    public EngineeringStandardsProfile CurrentProfile { get; set; } = EngineeringStandardsProfile.Default;

    public ValidationResult ValidateTicketForCompletion(TicketItem ticket)
    {
        var violations = new List<string>();

        if (ticket.Type == TicketType.Feature && CurrentProfile.RequireUnitTestsForFeatures)
        {
            var hasTestOrCoverage = ticket.AcceptanceCriteria.Any(ac => ac.Contains("test", StringComparison.OrdinalIgnoreCase) || ac.Contains("coverage", StringComparison.OrdinalIgnoreCase))
                || ticket.Deliverables.Any(d => d.ContentType == "csharp" && d.Name.Contains("Test", StringComparison.OrdinalIgnoreCase));

            if (!hasTestOrCoverage)
            {
                violations.Add("Feature ticket must specify and deliver automated unit tests (because 'It works on my machine' is not legally binding).");
            }
        }

        if (ticket.Type == TicketType.Bug)
        {
            if (CurrentProfile.RequireRcaForBugs && !ticket.Description.Contains("RCA", StringComparison.OrdinalIgnoreCase) && !ticket.AcceptanceCriteria.Any(ac => ac.Contains("cause", StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add("Bug ticket requires explicit Root Cause Analysis (RCA) — a formal explanation of why entropy occurred.");
            }

            if (CurrentProfile.RequireRegressionTestForBugs && !ticket.AcceptanceCriteria.Any(ac => ac.Contains("regression", StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add("Bug ticket requires an automated regression test so we don't look foolish when it recurs.");
            }
        }

        if (ticket.Type == TicketType.Epic && CurrentProfile.RequireAdrForEpics)
        {
            var hasAdr = ticket.AcceptanceCriteria.Any(ac => ac.Contains("ADR", StringComparison.OrdinalIgnoreCase))
                || ticket.Deliverables.Any(d => d.Name.Contains("ADR", StringComparison.OrdinalIgnoreCase));

            if (!hasAdr)
            {
                violations.Add("Epic requires an Architectural Decision Record (ADR) etched in the documentation temple.");
            }
        }

        return violations.Count == 0 ? ValidationResult.Success() : new ValidationResult(false, violations);
    }
}
