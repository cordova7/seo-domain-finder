using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;
using SeoDomainFinder.Core.Services;

namespace SeoDomainFinder.Core.Services;

public sealed class DomainSearchService : IDomainSearchService
{
    private readonly IEnumerable<INameGenerator> _generators;
    private readonly ISeoScorer _seoScorer;
    private readonly IDomainAvailabilityChecker _availabilityChecker;

    public DomainSearchService(
        IEnumerable<INameGenerator> generators,
        ISeoScorer seoScorer,
        IDomainAvailabilityChecker availabilityChecker)
    {
        _generators = generators;
        _seoScorer = seoScorer;
        _availabilityChecker = availabilityChecker;
    }

    public async Task<DomainSearchResult> SearchAsync(DomainSearchRequest request, CancellationToken ct = default)
    {
        var lang = KeywordExtractor.DetectLanguage(request.Prompt, request.Language);
        var keywords = KeywordExtractor.Extract(request.Prompt, lang);
        var tlds = request.Tlds
            .Select(t => t.Trim().TrimStart('.').ToLowerInvariant())
            .Where(NameSanitizer.IsAllowedTld)
            .Distinct()
            .ToList();

        if (tlds.Count == 0)
            tlds = ["com"];

        string generatorUsed = "heuristic";
        string? warning = null;
        IReadOnlyList<string> rawNames;

        if (request.UseLlm)
        {
            var llm = _generators.FirstOrDefault(g => g.Name == "openrouter")
                ?? _generators.FirstOrDefault(g => g.Name != "heuristic");
            if (llm is not null)
            {
                try
                {
                    rawNames = await llm.GenerateAsync(request, ct);
                    generatorUsed = llm.Name;
                }
                catch (Exception ex)
                {
                    warning = $"LLM failed, using heuristics: {ex.Message}";
                    rawNames = await _generators.First(g => g.Name == "heuristic").GenerateAsync(request, ct);
                }
            }
            else
            {
                rawNames = await _generators.First(g => g.Name == "heuristic").GenerateAsync(request, ct);
            }
        }
        else
        {
            rawNames = await _generators.First(g => g.Name == "heuristic").GenerateAsync(request, ct);
        }

        var scored = new List<DomainCandidate>();
        foreach (var name in rawNames.Where(NameSanitizer.IsValidDomainName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var seo = _seoScorer.Score(name, keywords, lang);
            foreach (var tld in tlds)
            {
                scored.Add(new DomainCandidate
                {
                    Name = name,
                    Tld = tld,
                    SeoScore = seo.Score,
                    SeoExplanation = seo.Explanation
                });
            }
        }

        scored = scored
            .OrderByDescending(c => c.SeoScore)
            .ThenBy(c => c.Name.Length)
            .Take(request.MaxCandidates * tlds.Count)
            .ToList();

        var checkedDomains = new List<DomainCandidate>();
        foreach (var candidate in scored)
        {
            ct.ThrowIfCancellationRequested();
            var check = await _availabilityChecker.CheckAsync(candidate.FullDomain, ct);
            candidate.Available = check.Available;
            candidate.PriceUsd = check.PriceUsd;
            candidate.PriceType = check.PriceType;
            candidate.UnavailableReason = check.Reason;

            var priceOk = check.PriceUsd is null || check.PriceUsd <= request.MaxPriceUsd;
            var typeOk = check.PriceType is null or "standard" or "registration";

            if (check.Available && priceOk && typeOk)
            {
                candidate.TotalScore = candidate.SeoScore + (priceOk ? 10 : 0);
                checkedDomains.Add(candidate);
            }
            else if (!check.Available || !priceOk || !typeOk)
            {
                candidate.TotalScore = candidate.SeoScore;
                if (!check.Available && check.PriceType == "premium")
                    candidate.UnavailableReason = "premium domain";
                else if (!priceOk)
                    candidate.UnavailableReason = $"price ${check.PriceUsd:F2} exceeds max ${request.MaxPriceUsd:F2}";
            }
        }

        var ranked = checkedDomains
            .Where(c => c.Available == true)
            .OrderByDescending(c => c.TotalScore)
            .ThenBy(c => c.PriceUsd)
            .Take(request.MaxCandidates)
            .ToList();

        if (ranked.Count == 0)
        {
            ranked = scored
                .Take(request.MaxCandidates)
                .Select(c =>
                {
                    c.TotalScore = c.SeoScore;
                    return c;
                })
                .ToList();
            warning = (warning ?? "") + " No available domains within price limit; showing SEO-ranked candidates without availability.";
        }

        return new DomainSearchResult
        {
            Candidates = ranked,
            GeneratorUsed = generatorUsed,
            ExtractedKeywords = keywords,
            Warning = string.IsNullOrWhiteSpace(warning) ? null : warning.Trim()
        };
    }
}
