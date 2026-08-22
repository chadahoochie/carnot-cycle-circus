namespace CarnotCycleCircus.Core.Domain.Events;

public record ArtifactItem(
    string Name,
    string Content,
    string ContentType = "markdown",
    string? Description = null
);
