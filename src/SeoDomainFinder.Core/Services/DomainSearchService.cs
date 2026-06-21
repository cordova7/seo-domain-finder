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

    public async Task<DomainSearchResult> SearchAsync(
        DomainSearchRequest request,
        IProgress<SearchProgressEvent>? progress = null,
        CancellationToken ct = default)
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
        var maxChecks = Math.Max(10, request.MaxChecks);
        var target = request.MaxCandidates;

        Report(progress, "generating", 0, maxChecks, 0, null);

        var genRequest = new DomainSearchRequest
        {
            Prompt = request.Prompt,
            Language = request.Language,
            Tlds = tlds,
            MaxPriceUsd = request.MaxPriceUsd,
            UseLlm = request.UseLlm,
            OpenRouterApiKey = request.OpenRouterApiKey,
            PorkbunApiKey = request.PorkbunApiKey,
            PorkbunSecretKey = request.PorkbunSecretKey,
            MaxCandidates = request.MaxCandidates * 4,
            MaxChecks = request.MaxChecks
        };

        IReadOnlyList<string> rawNames;
        if (request.UseLlm)
        {
            var llm = _generators.FirstOrDefault(g => g.Name == "openrouter")
                ?? _generators.FirstOrDefault(g => g.Name != "heuristic");
            if (llm is not null)
            {
                try
                {
                    rawNames = await llm.GenerateAsync(genRequest, ct);
                    generatorUsed = llm.Name;
                }
                catch (Exception ex)
                {
                    warning = $"LLM failed, using heuristics: {ex.Message}";
                    rawNames = await GetHeuristic().GenerateAsync(genRequest, ct);
                }
            }
            else
            {
                rawNames = await GetHeuristic().GenerateAsync(genRequest, ct);
            }
        }
        else
        {
            rawNames = await GetHeuristic().GenerateAsync(genRequest, ct);
        }

        var queue = BuildCandidateQueue(rawNames, keywords, lang, tlds);
        var available = new List<DomainCandidate>();
        var checksUsed = 0;
        var credentialFailures = 0;
        var rateLimitFailures = 0;

        Report(progress, "checking", 0, maxChecks, 0, null);

        var attempts = 0;
        foreach (var candidate in queue)
        {
            if (available.Count >= target || checksUsed >= maxChecks || attempts >= maxChecks)
                break;

            ct.ThrowIfCancellationRequested();
            attempts++;

            Report(progress, "checking", checksUsed, maxChecks, available.Count, candidate.FullDomain);

            var check = await _availabilityChecker.CheckAsync(candidate.FullDomain, ct);

            if (IsRateLimited(check))
            {
                rateLimitFailures++;
                continue;
            }

            checksUsed++;

            if (string.Equals(check.Reason, DomainCheckReasons.CredentialsMissing, StringComparison.OrdinalIgnoreCase))
                credentialFailures++;

            var priceOk = check.PriceUsd is null || check.PriceUsd <= request.MaxPriceUsd;
            var typeOk = check.PriceType is null or "standard" or "registration";

            if (!check.Available || !priceOk || !typeOk)
                continue;

            candidate.Available = true;
            candidate.PriceUsd = check.PriceUsd;
            candidate.PriceType = check.PriceType;
            candidate.TotalScore = candidate.SeoScore + 10;
            available.Add(candidate);

            Report(progress, "checking", checksUsed, maxChecks, available.Count, candidate.FullDomain);
        }

        if (available.Count == 0)
        {
            if (checksUsed > 0 && credentialFailures == checksUsed)
            {
                warning = "Domain availability checks are unavailable (Porkbun API not configured on server).";
            }
            else if (rateLimitFailures > 0 && checksUsed == 0)
            {
                warning = "Many checks hit Porkbun rate limits. Please try again in a minute.";
            }
            else if (rateLimitFailures > checksUsed / 2)
            {
                warning = (warning ?? "") +
                          " Many checks hit Porkbun rate limits. Results may be incomplete.";
            }
            else
            {
                warning = (warning ?? "") +
                          $" Checked {checksUsed} domains; none available under ${request.MaxPriceUsd:F0}. " +
                          "Try shorter names, add a country TLD (e.g. .mx), raise the price limit, or enable AI.";
            }
        }

        var ranked = available
            .OrderByDescending(c => c.TotalScore)
            .ThenBy(c => c.PriceUsd)
            .Take(target)
            .ToList();

        var result = new DomainSearchResult
        {
            Candidates = ranked,
            GeneratorUsed = generatorUsed,
            ExtractedKeywords = keywords,
            Warning = string.IsNullOrWhiteSpace(warning) ? null : warning.Trim()
        };

        Report(progress, "done", checksUsed, maxChecks, ranked.Count, null);
        return result;
    }

    private static bool IsRateLimited(DomainCheckResult check) =>
        string.Equals(check.Reason, DomainCheckReasons.RateLimited, StringComparison.OrdinalIgnoreCase) ||
        (check.Reason?.Contains("rate_limited", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (check.Reason?.Contains("within 10 seconds", StringComparison.OrdinalIgnoreCase) ?? false);

    private static void Report(
        IProgress<SearchProgressEvent>? progress,
        string phase,
        int checksUsed,
        int maxChecks,
        int foundCount,
        string? currentDomain)
    {
        progress?.Report(new SearchProgressEvent(
            phase,
            checksUsed,
            maxChecks,
            foundCount,
            currentDomain,
            phase == "checking" || phase == "done"
                ? Math.Max(0, (maxChecks - checksUsed) * 10)
                : null));
    }

    private INameGenerator GetHeuristic() =>
        _generators.First(g => g.Name == "heuristic");

    private List<DomainCandidate> BuildCandidateQueue(
        IReadOnlyList<string> rawNames,
        IReadOnlyList<string> keywords,
        string lang,
        IReadOnlyList<string> tlds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<DomainCandidate>();

        foreach (var name in rawNames.Where(NameSanitizer.IsValidDomainName))
        {
            var seo = _seoScorer.Score(name, keywords, lang);
            foreach (var tld in tlds)
            {
                var key = $"{name}.{tld}";
                if (!seen.Add(key))
                    continue;
                list.Add(new DomainCandidate
                {
                    Name = name,
                    Tld = tld,
                    SeoScore = seo.Score,
                    SeoExplanation = seo.Explanation
                });
            }
        }

        return list
            .OrderByDescending(c => c.SeoScore)
            .ThenBy(c => c.Name.Length)
            .ToList();
    }
}
