namespace SeoDomainFinder.Core.Models;

public sealed record FoundEntry(string Domain, decimal? PriceUsd, int SeoScore);

public sealed record SearchSummary(
    string Prompt,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Tlds,
    decimal MaxPriceUsd,
    int ChecksUsed,
    int MaxChecks,
    IReadOnlyList<FoundEntry> Found,
    IReadOnlyList<string> SampleUnavailable,
    int UnavailableCount,
    int PremiumSkipped,
    bool RefillTriggered);
