namespace SeoDomainFinder.Core.Models;

public sealed class DomainCandidate
{
    public required string Name { get; init; }
    public required string Tld { get; init; }
    public string FullDomain => $"{Name}.{Tld}";
    public int SeoScore { get; init; }
    public string SeoExplanation { get; init; } = "";
    public bool? Available { get; set; }
    public decimal? PriceUsd { get; set; }
    public string? PriceType { get; set; }
    public int TotalScore { get; set; }
    public string? UnavailableReason { get; set; }
}
