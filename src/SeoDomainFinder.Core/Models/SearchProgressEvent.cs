namespace SeoDomainFinder.Core.Models;

public sealed record SearchProgressFoundCandidate(
    string Name,
    string Tld,
    string FullDomain,
    int SeoScore,
    string SeoExplanation,
    decimal? PriceUsd);

public sealed record SearchProgressEvent(
    string Phase,
    int ChecksUsed,
    int MaxChecks,
    int FoundCount,
    string? CurrentDomain,
    int? EtaSeconds,
    SearchProgressFoundCandidate? FoundCandidate = null);
