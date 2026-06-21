namespace SeoDomainFinder.Api.Contracts;

public sealed record DomainSearchDto(
    string Prompt,
    string? Language,
    IReadOnlyList<string>? Tlds,
    decimal? MaxPriceUsd,
    bool UseLlm,
    string? OpenRouterApiKey,
    string? PorkbunApiKey,
    string? PorkbunSecretKey,
    int? MaxCandidates,
    int? MaxChecks);

public sealed record DomainCandidateDto(
    string Name,
    string Tld,
    string FullDomain,
    int SeoScore,
    string SeoExplanation,
    bool? Available,
    decimal? PriceUsd,
    string? PriceType,
    int TotalScore,
    string? UnavailableReason);

public sealed record DomainSearchResponseDto(
    IReadOnlyList<DomainCandidateDto> Candidates,
    string GeneratorUsed,
    IReadOnlyList<string> ExtractedKeywords,
    string? Warning,
    string? Advice);
