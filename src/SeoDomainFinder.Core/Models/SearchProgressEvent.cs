namespace SeoDomainFinder.Core.Models;

public sealed record SearchProgressEvent(
    string Phase,
    int ChecksUsed,
    int MaxChecks,
    int FoundCount,
    string? CurrentDomain,
    int? EtaSeconds);
