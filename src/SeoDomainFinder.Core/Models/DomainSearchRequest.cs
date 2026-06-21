namespace SeoDomainFinder.Core.Models;

public sealed class DomainSearchRequest
{
    public required string Prompt { get; init; }
    public string? Language { get; init; }
    public IReadOnlyList<string> Tlds { get; init; } = ["com"];
    public decimal MaxPriceUsd { get; init; } = 15m;
    public bool UseLlm { get; init; }
    public string? OpenRouterApiKey { get; init; }
    public string? PorkbunApiKey { get; init; }
    public string? PorkbunSecretKey { get; init; }
    public int MaxCandidates { get; init; } = 15;
    public int MaxChecks { get; init; } = 90;
}
