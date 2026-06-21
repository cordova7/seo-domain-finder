namespace SeoDomainFinder.Core.Models;

public sealed class DomainSearchResult
{
    public required IReadOnlyList<DomainCandidate> Candidates { get; init; }
    public required string GeneratorUsed { get; init; }
    public IReadOnlyList<string> ExtractedKeywords { get; init; } = [];
    public string? Warning { get; init; }
}
