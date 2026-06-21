namespace SeoDomainFinder.Core.Abstractions;

public sealed record PlannedCheck(string Label, string Tld, int Score);

public sealed record CheckPlannerRequest(
    string Prompt,
    string Language,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Tlds,
    int MaxChecks,
    decimal MaxPriceUsd,
    IReadOnlyList<string> SeedNames,
    string? OpenRouterApiKey,
    IReadOnlyList<string>? TakenSample = null,
    int? RemainingChecks = null);

public interface ICheckPlanner
{
    Task<IReadOnlyList<PlannedCheck>> PlanAsync(CheckPlannerRequest request, CancellationToken ct = default);
}
