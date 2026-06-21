namespace SeoDomainFinder.Core.Abstractions;

public sealed record DomainCheckResult(
    string FullDomain,
    bool Available,
    decimal? PriceUsd,
    string? PriceType,
    string? Reason);

public interface IDomainAvailabilityChecker
{
    Task<DomainCheckResult> CheckAsync(string fullDomain, CancellationToken ct = default);
}
