namespace SeoDomainFinder.Core.Models;

public sealed record SearchBrief(
    string ProductSummary,
    string Audience,
    IReadOnlyList<string> Vibe,
    IReadOnlyList<string> NamingStyles,
    IReadOnlyList<string> ConceptKeywords,
    IReadOnlyList<string> AvoidTerms,
    IReadOnlyList<string> AvoidPatterns,
    string TldStrategy);
