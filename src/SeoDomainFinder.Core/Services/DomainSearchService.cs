using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Services;

public sealed class DomainSearchService : IDomainSearchService
{
    private const int RefillCheckThreshold = 15;
    private const int RefillMinFound = 3;
    private const int RefillMinRemaining = 5;

    private readonly IEnumerable<INameGenerator> _generators;
    private readonly ISeoScorer _seoScorer;
    private readonly IDomainAvailabilityChecker _availabilityChecker;
    private readonly ICheckPlanner? _checkPlanner;
    private readonly IDomainAdvisor? _domainAdvisor;

    public DomainSearchService(
        IEnumerable<INameGenerator> generators,
        ISeoScorer seoScorer,
        IDomainAvailabilityChecker availabilityChecker,
        ICheckPlanner? checkPlanner = null,
        IDomainAdvisor? domainAdvisor = null)
    {
        _generators = generators;
        _seoScorer = seoScorer;
        _availabilityChecker = availabilityChecker;
        _checkPlanner = checkPlanner;
        _domainAdvisor = domainAdvisor;
    }

    public async Task<DomainSearchResult> SearchAsync(
        DomainSearchRequest request,
        IProgress<SearchProgressEvent>? progress = null,
        CancellationToken ct = default)
    {
        var lang = KeywordExtractor.DetectLanguage(request.Prompt, request.Language);
        var keywords = KeywordExtractor.Extract(request.Prompt, lang);
        var tlds = NormalizeTlds(request.Tlds);

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

        var nameGen = await GenerateNamesAsync(request, genRequest, ct);
        generatorUsed = nameGen.GeneratorUsed;
        warning = nameGen.Warning;

        var queueResult = await BuildQueueAsync(
            progress, request, nameGen.Names, keywords, lang, tlds, maxChecks, generatorUsed, warning, ct);
        var queue = queueResult.Queue;
        generatorUsed = queueResult.GeneratorUsed;
        warning = queueResult.Warning;

        var available = new List<DomainCandidate>();
        var checksUsed = 0;
        var credentialFailures = 0;
        var rateLimitFailures = 0;
        var unavailableCount = 0;
        var premiumSkipped = 0;
        var unavailableSample = new List<string>();
        var refillTriggered = false;

        Report(progress, "checking", 0, maxChecks, 0, null);

        var attempts = 0;
        for (var i = 0; i < queue.Count; i++)
        {
            if (available.Count >= target || checksUsed >= maxChecks || attempts >= maxChecks)
                break;

            if (!refillTriggered &&
                request.UseLlm &&
                _checkPlanner is not null &&
                checksUsed >= RefillCheckThreshold &&
                available.Count < RefillMinFound &&
                maxChecks - checksUsed >= RefillMinRemaining)
            {
                refillTriggered = true;
                Report(progress, "refining", checksUsed, maxChecks, available.Count, null);

                try
                {
                    var refill = await _checkPlanner.PlanAsync(new CheckPlannerRequest(
                        request.Prompt,
                        lang,
                        keywords,
                        tlds,
                        maxChecks,
                        request.MaxPriceUsd,
                        nameGen.Names.ToList(),
                        request.OpenRouterApiKey,
                        unavailableSample.ToList(),
                        maxChecks - checksUsed), ct);

                    queue.AddRange(PlannedToCandidates(refill, keywords, lang));
                }
                catch (Exception ex)
                {
                    warning = AppendWarning(warning, $"AI refill failed: {ex.Message}");
                }
            }

            var candidate = queue[i];
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

            if (string.Equals(check.Reason, DomainCheckReasons.Premium, StringComparison.OrdinalIgnoreCase))
            {
                premiumSkipped++;
                continue;
            }

            if (!check.Available)
            {
                unavailableCount++;
                if (unavailableSample.Count < 12)
                    unavailableSample.Add(candidate.FullDomain);
                continue;
            }

            var priceOk = check.PriceUsd is null || check.PriceUsd <= request.MaxPriceUsd;
            var typeOk = check.PriceType is null or "standard" or "registration";

            if (!priceOk || !typeOk)
            {
                unavailableCount++;
                if (unavailableSample.Count < 12)
                    unavailableSample.Add(candidate.FullDomain);
                continue;
            }

            candidate.Available = true;
            candidate.PriceUsd = check.PriceUsd;
            candidate.PriceType = check.PriceType;
            candidate.TotalScore = candidate.SeoScore + 10;
            available.Add(candidate);

            ReportFound(progress, checksUsed, maxChecks, available.Count, candidate);
        }

        warning = BuildAvailabilityWarning(
            warning, available, checksUsed, credentialFailures, rateLimitFailures, request.MaxPriceUsd);

        var ranked = available
            .OrderByDescending(c => c.TotalScore)
            .ThenBy(c => c.PriceUsd)
            .Take(target)
            .ToList();

        var summary = new SearchSummary(
            request.Prompt,
            keywords,
            tlds,
            request.MaxPriceUsd,
            checksUsed,
            maxChecks,
            ranked.Select(c => new FoundEntry(c.FullDomain, c.PriceUsd, c.SeoScore)).ToList(),
            unavailableSample,
            unavailableCount,
            premiumSkipped,
            refillTriggered);

        string? advice = null;
        if (request.UseLlm && _domainAdvisor is not null)
        {
            Report(progress, "advising", checksUsed, maxChecks, ranked.Count, null);
            try
            {
                advice = await _domainAdvisor.AdviseAsync(summary, request.OpenRouterApiKey, ct);
            }
            catch (Exception ex)
            {
                warning = AppendWarning(warning, $"AI advice failed: {ex.Message}");
            }
        }

        var result = new DomainSearchResult
        {
            Candidates = ranked,
            GeneratorUsed = generatorUsed,
            ExtractedKeywords = keywords,
            Warning = string.IsNullOrWhiteSpace(warning) ? null : warning.Trim(),
            Advice = advice
        };

        Report(progress, "done", checksUsed, maxChecks, ranked.Count, null);
        return result;
    }

    private sealed record NameGenResult(
        IReadOnlyList<string> Names,
        string GeneratorUsed,
        string? Warning);

    private sealed record QueueBuildResult(
        List<DomainCandidate> Queue,
        string GeneratorUsed,
        string? Warning);

    private async Task<NameGenResult> GenerateNamesAsync(
        DomainSearchRequest request,
        DomainSearchRequest genRequest,
        CancellationToken ct)
    {
        if (!request.UseLlm)
        {
            var names = await GetHeuristic().GenerateAsync(genRequest, ct);
            return new NameGenResult(names, "heuristic", null);
        }

        var heuristicNames = await GetHeuristic().GenerateAsync(genRequest, ct);
        var merged = new HashSet<string>(heuristicNames, StringComparer.OrdinalIgnoreCase);
        string? warning = null;
        var generatorUsed = "heuristic";

        // Planner invents check targets; skip slow redundant OpenRouter name gen (avoids 60s timeout).
        var skipLlmNameGen = _checkPlanner is not null;
        if (skipLlmNameGen)
            return new NameGenResult(merged.ToList(), generatorUsed, null);

        var llm = _generators.FirstOrDefault(g => g.Name == "openrouter");
        if (llm is not null)
        {
            generatorUsed = "hybrid";
            try
            {
                foreach (var name in await llm.GenerateAsync(genRequest, ct))
                    merged.Add(name);
            }
            catch (Exception ex)
            {
                warning = AppendWarning(warning, $"LLM name generation failed: {ex.Message}");
                generatorUsed = "heuristic";
            }
        }

        return new NameGenResult(merged.ToList(), generatorUsed, warning);
    }

    private async Task<QueueBuildResult> BuildQueueAsync(
        IProgress<SearchProgressEvent>? progress,
        DomainSearchRequest request,
        IReadOnlyList<string> rawNames,
        IReadOnlyList<string> keywords,
        string lang,
        IReadOnlyList<string> tlds,
        int maxChecks,
        string generatorUsed,
        string? warning,
        CancellationToken ct)
    {
        if (request.UseLlm && _checkPlanner is not null)
        {
            try
            {
                Report(progress, "planning", 0, maxChecks, 0, null);
                var planned = await _checkPlanner.PlanAsync(new CheckPlannerRequest(
                    request.Prompt,
                    lang,
                    keywords,
                    tlds,
                    maxChecks,
                    request.MaxPriceUsd,
                    rawNames.ToList(),
                    request.OpenRouterApiKey), ct);

                if (planned.Count > 0)
                {
                    var used = generatorUsed is "hybrid" or "heuristic"
                        ? $"{generatorUsed}+planner"
                        : "planner";
                    return new QueueBuildResult(
                        PlannedToCandidates(planned, keywords, lang), used, warning);
                }

                warning = AppendWarning(warning,
                    "AI planner returned no valid domains; using heuristic queue.");
            }
            catch (Exception ex)
            {
                warning = AppendWarning(warning, $"AI planner failed, using heuristic queue: {ex.Message}");
            }
        }

        return new QueueBuildResult(
            BuildCandidateQueue(rawNames, keywords, lang, tlds), generatorUsed, warning);
    }

    private List<DomainCandidate> PlannedToCandidates(
        IReadOnlyList<PlannedCheck> planned,
        IReadOnlyList<string> keywords,
        string lang)
    {
        var list = new List<DomainCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in planned)
        {
            if (!NameSanitizer.IsValidDomainName(item.Label))
                continue;

            var key = $"{item.Label}.{item.Tld}";
            if (!seen.Add(key))
                continue;

            var seo = _seoScorer.Score(item.Label, keywords, lang);
            list.Add(new DomainCandidate
            {
                Name = item.Label,
                Tld = item.Tld,
                SeoScore = Math.Max(seo.Score, item.Score),
                SeoExplanation = seo.Explanation
            });
        }

        return list;
    }

    private static List<string> NormalizeTlds(IReadOnlyList<string> requestTlds)
    {
        var tlds = requestTlds
            .Select(t => t.Trim().TrimStart('.').ToLowerInvariant())
            .Where(NameSanitizer.IsAllowedTld)
            .Distinct()
            .ToList();

        return tlds.Count == 0 ? ["com"] : tlds;
    }

    private static bool IsRateLimited(DomainCheckResult check) =>
        string.Equals(check.Reason, DomainCheckReasons.RateLimited, StringComparison.OrdinalIgnoreCase) ||
        (check.Reason?.Contains("rate_limited", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (check.Reason?.Contains("within 10 seconds", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string? AppendWarning(string? warning, string message) =>
        string.IsNullOrWhiteSpace(warning) ? message : $"{warning} {message}";

    private static string? BuildAvailabilityWarning(
        string? warning,
        List<DomainCandidate> available,
        int checksUsed,
        int credentialFailures,
        int rateLimitFailures,
        decimal maxPriceUsd)
    {
        if (available.Count > 0)
        {
            if (rateLimitFailures > checksUsed / 2)
                return AppendWarning(warning, "Many checks hit Porkbun rate limits. Results may be incomplete.");
            return warning;
        }

        if (checksUsed > 0 && credentialFailures == checksUsed)
        {
            return AppendWarning(warning,
                "Domain availability checks are unavailable (Porkbun API not configured on server).");
        }

        if (rateLimitFailures > 0 && checksUsed == 0)
        {
            return AppendWarning(warning,
                "Many checks hit Porkbun rate limits. Please try again in a minute.");
        }

        if (rateLimitFailures > checksUsed / 2)
        {
            return AppendWarning(warning,
                "Many checks hit Porkbun rate limits. Results may be incomplete.");
        }

        return AppendWarning(warning,
            $"Checked {checksUsed} domains; none available under ${maxPriceUsd:F0}. " +
            "Try shorter names, add a country TLD (e.g. .mx), raise the price limit, or enable AI.");
    }

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
            phase is "checking" or "done" or "advising"
                ? Math.Max(0, (maxChecks - checksUsed) * 10)
                : null));
    }

    private static void ReportFound(
        IProgress<SearchProgressEvent>? progress,
        int checksUsed,
        int maxChecks,
        int foundCount,
        DomainCandidate candidate)
    {
        progress?.Report(new SearchProgressEvent(
            "found",
            checksUsed,
            maxChecks,
            foundCount,
            candidate.FullDomain,
            Math.Max(0, (maxChecks - checksUsed) * 10),
            new SearchProgressFoundCandidate(
                candidate.Name,
                candidate.Tld,
                candidate.FullDomain,
                candidate.SeoScore,
                candidate.SeoExplanation,
                candidate.PriceUsd)));
    }

    private INameGenerator GetHeuristic() =>
        _generators.First(g => g.Name == "heuristic");

    internal List<DomainCandidate> BuildCandidateQueue(
        IReadOnlyList<string> rawNames,
        IReadOnlyList<string> keywords,
        string lang,
        IReadOnlyList<string> tlds)
    {
        var orderedTlds = OrderTldsComFirst(tlds);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<DomainCandidate>();

        foreach (var name in rawNames.Where(NameSanitizer.IsValidDomainName))
        {
            var seo = _seoScorer.Score(name, keywords, lang);
            foreach (var tld in orderedTlds)
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
            .OrderByDescending(c => c.Tld == "com" ? 1 : 0)
            .ThenByDescending(c => c.SeoScore)
            .ThenBy(c => c.Name.Length)
            .ToList();
    }

    internal static IReadOnlyList<string> OrderTldsComFirst(IReadOnlyList<string> tlds) =>
        tlds.OrderBy(t => t == "com" ? 0 : 1).ToList();
}
