using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Services;

public sealed class DomainSearchService : IDomainSearchService
{
    private const int RefillCheckThreshold = 15;
    private const int RefillMinRemaining = 5;
    private const int RecoveryReserve = 8;
    private const int RecoveryMinFound = 2;

    private readonly IEnumerable<INameGenerator> _generators;
    private readonly ISeoScorer _seoScorer;
    private readonly IDomainAvailabilityChecker _availabilityChecker;
    private readonly ICheckPlanner? _checkPlanner;
    private readonly IDomainAdvisor? _domainAdvisor;
    private readonly IBriefGenerator? _briefGenerator;

    public DomainSearchService(
        IEnumerable<INameGenerator> generators,
        ISeoScorer seoScorer,
        IDomainAvailabilityChecker availabilityChecker,
        ICheckPlanner? checkPlanner = null,
        IDomainAdvisor? domainAdvisor = null,
        IBriefGenerator? briefGenerator = null)
    {
        _generators = generators;
        _seoScorer = seoScorer;
        _availabilityChecker = availabilityChecker;
        _checkPlanner = checkPlanner;
        _domainAdvisor = domainAdvisor;
        _briefGenerator = briefGenerator;
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
        var useBriefPath = request.UseLlm && _briefGenerator is not null && _checkPlanner is not null;

        SearchBrief? brief = null;
        if (useBriefPath)
        {
            Report(progress, "briefing", 0, maxChecks, 0, null);
            try
            {
                brief = await _briefGenerator!.GenerateAsync(new BriefGeneratorRequest(
                    request.Prompt, lang, tlds, request.OpenRouterApiKey), ct);
            }
            catch (Exception ex)
            {
                brief = SearchBriefFallback.Create(request.Prompt, lang, keywords);
                warning = AppendWarning(warning, $"AI brief failed, using fallback: {ex.Message}");
            }
        }
        else
        {
            Report(progress, "generating", 0, maxChecks, 0, null);
        }

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

        var nameGen = useBriefPath
            ? new NameGenResult([], "brief", warning)
            : await GenerateNamesAsync(request, genRequest, ct);
        generatorUsed = nameGen.GeneratorUsed;
        warning = nameGen.Warning ?? warning;

        var queueResult = await BuildQueueAsync(
            progress, request, nameGen.Names, keywords, lang, tlds, maxChecks, generatorUsed, warning, brief, ct);
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
        var state = new CheckLoopState
        {
            RefillTriggered = false,
            Warning = warning
        };

        Report(progress, "checking", 0, maxChecks, 0, null);

        var queueIndex = 0;
        while (queueIndex < queue.Count)
        {
            if (available.Count >= target || state.ChecksUsed >= maxChecks || state.Attempts >= maxChecks)
                break;

            var candidate = queue[queueIndex++];
            await ProcessCandidateAsync(
                candidate, progress, request, keywords, lang, tlds, maxChecks, target, queue,
                available, unavailableSample, state, brief, ct);
        }

        warning = state.Warning;

        var recoveryTriggered = false;
        if (useBriefPath &&
            available.Count < RecoveryMinFound &&
            state.ChecksUsed < maxChecks &&
            maxChecks - state.ChecksUsed >= RefillMinRemaining)
        {
            recoveryTriggered = true;
            var recoveryCandidates = await TryRecoveryPlannerAsync(
                request, keywords, lang, tlds, maxChecks, queue, unavailableSample,
                available, state.ChecksUsed, progress, brief, ct);

            foreach (var candidate in recoveryCandidates)
            {
                if (available.Count >= target || state.ChecksUsed >= maxChecks || state.Attempts >= maxChecks)
                    break;

                await ProcessCandidateAsync(
                    candidate, progress, request, keywords, lang, tlds, maxChecks, target, queue,
                    available, unavailableSample, state, brief, ct);
            }
        }

        warning = state.Warning;
        checksUsed = state.ChecksUsed;
        credentialFailures = state.CredentialFailures;
        rateLimitFailures = state.RateLimitFailures;
        unavailableCount = state.UnavailableCount;
        premiumSkipped = state.PremiumSkipped;
        var refillTriggered = state.RefillTriggered;

        if (recoveryTriggered && generatorUsed == "brief+planner")
            generatorUsed = "brief+planner+recovery";

        warning = BuildAvailabilityWarning(
            warning, available, checksUsed, credentialFailures, rateLimitFailures,
            request.MaxPriceUsd, request.UseLlm);

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
            refillTriggered,
            brief);

        string? advice = null;
        if (request.UseLlm && _domainAdvisor is not null)
        {
            if (ranked.Count >= 2)
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
            else
            {
                advice = BuildShortAdvice(ranked, checksUsed, maxChecks, unavailableCount);
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
        SearchBrief? brief,
        CancellationToken ct)
    {
        if (request.UseLlm && _checkPlanner is not null)
        {
            try
            {
                Report(progress, "planning", 0, maxChecks, 0, null);

                var queue = await TryBuildPlannerQueueAsync(
                    request, rawNames, keywords, lang, tlds, maxChecks, brief, ct);

                if (queue.Count > 0)
                {
                    var used = brief is not null
                        ? "brief+planner"
                        : generatorUsed is "hybrid" or "heuristic"
                            ? $"{generatorUsed}+planner"
                            : "planner";
                    return new QueueBuildResult(queue, used, warning);
                }

                if (brief is not null)
                {
                    warning = AppendWarning(warning,
                        "AI planner returned no valid domains; checking heuristic names.");
                }
                else
                {
                    warning = AppendWarning(warning,
                        "AI planner returned no valid domains; using heuristic queue.");
                }
            }
            catch (Exception ex)
            {
                warning = AppendWarning(warning,
                    brief is not null
                        ? $"AI planner failed: {ex.Message}; checking heuristic names."
                        : $"AI planner failed, using heuristic queue: {ex.Message}");
            }
        }

        return new QueueBuildResult(
            await BuildFallbackQueueAsync(request, rawNames, keywords, lang, tlds, ct),
            generatorUsed,
            warning);
    }

    private async Task<List<DomainCandidate>> BuildFallbackQueueAsync(
        DomainSearchRequest request,
        IReadOnlyList<string> rawNames,
        IReadOnlyList<string> keywords,
        string lang,
        IReadOnlyList<string> tlds,
        CancellationToken ct)
    {
        if (rawNames.Count > 0)
            return BuildCandidateQueue(rawNames, keywords, lang, tlds);

        var heuristicNames = await GetHeuristic().GenerateAsync(new DomainSearchRequest
        {
            Prompt = request.Prompt,
            Language = request.Language,
            Tlds = tlds,
            MaxPriceUsd = request.MaxPriceUsd,
            UseLlm = false,
            MaxCandidates = request.MaxCandidates * 4,
            MaxChecks = request.MaxChecks
        }, ct);

        return BuildCandidateQueue(heuristicNames, keywords, lang, tlds);
    }

    private sealed class CheckLoopState
    {
        public int ChecksUsed;
        public int Attempts;
        public int CredentialFailures;
        public int RateLimitFailures;
        public int UnavailableCount;
        public int PremiumSkipped;
        public bool RefillTriggered;
        public string? Warning;
    }

    private async Task ProcessCandidateAsync(
        DomainCandidate candidate,
        IProgress<SearchProgressEvent>? progress,
        DomainSearchRequest request,
        IReadOnlyList<string> keywords,
        string lang,
        IReadOnlyList<string> tlds,
        int maxChecks,
        int target,
        List<DomainCandidate> queue,
        List<DomainCandidate> available,
        List<string> unavailableSample,
        CheckLoopState state,
        SearchBrief? brief,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        state.Attempts++;

        Report(progress, "checking", state.ChecksUsed, maxChecks, available.Count, candidate.FullDomain);

        var check = await _availabilityChecker.CheckAsync(candidate.FullDomain, ct);

        if (IsRateLimited(check))
        {
            state.RateLimitFailures++;
            return;
        }

        state.ChecksUsed++;

        if (string.Equals(check.Reason, DomainCheckReasons.CredentialsMissing, StringComparison.OrdinalIgnoreCase))
            state.CredentialFailures++;

        if (string.Equals(check.Reason, DomainCheckReasons.Premium, StringComparison.OrdinalIgnoreCase))
        {
            state.PremiumSkipped++;
            ApplyRefillOutcome(await TryRefillAsync(
                request, keywords, lang, tlds, maxChecks, queue, unavailableSample,
                state.RefillTriggered, available, state.ChecksUsed, progress, brief, ct),
                state);
            return;
        }

        if (!check.Available)
        {
            state.UnavailableCount++;
            if (unavailableSample.Count < 12)
                unavailableSample.Add(candidate.FullDomain);
            ApplyRefillOutcome(await TryRefillAsync(
                request, keywords, lang, tlds, maxChecks, queue, unavailableSample,
                state.RefillTriggered, available, state.ChecksUsed, progress, brief, ct),
                state);
            return;
        }

        var priceOk = check.PriceUsd is null || check.PriceUsd <= request.MaxPriceUsd;
        var typeOk = check.PriceType is null or "standard" or "registration";

        if (!priceOk || !typeOk)
        {
            state.UnavailableCount++;
            if (unavailableSample.Count < 12)
                unavailableSample.Add(candidate.FullDomain);
            ApplyRefillOutcome(await TryRefillAsync(
                request, keywords, lang, tlds, maxChecks, queue, unavailableSample,
                state.RefillTriggered, available, state.ChecksUsed, progress, brief, ct),
                state);
            return;
        }

        candidate.Available = true;
        candidate.PriceUsd = check.PriceUsd;
        candidate.PriceType = check.PriceType;
        candidate.TotalScore = candidate.SeoScore + 10;
        available.Add(candidate);

        ReportFound(progress, state.ChecksUsed, maxChecks, available.Count, candidate);

        ApplyRefillOutcome(await TryRefillAsync(
            request, keywords, lang, tlds, maxChecks, queue, unavailableSample,
            state.RefillTriggered, available, state.ChecksUsed, progress, brief, ct),
            state);
    }

    private async Task<List<DomainCandidate>> TryBuildPlannerQueueAsync(
        DomainSearchRequest request,
        IReadOnlyList<string> rawNames,
        IReadOnlyList<string> keywords,
        string lang,
        IReadOnlyList<string> tlds,
        int maxChecks,
        SearchBrief? brief,
        CancellationToken ct)
    {
        var seeds = brief is not null ? [] : rawNames.ToList();
        var initialBudget = brief is not null ? Math.Max(12, maxChecks - RecoveryReserve) : (int?)null;
        var planned = await _checkPlanner!.PlanAsync(new CheckPlannerRequest(
            request.Prompt,
            lang,
            keywords,
            tlds,
            maxChecks,
            request.MaxPriceUsd,
            seeds,
            request.OpenRouterApiKey,
            RemainingChecks: initialBudget,
            Brief: brief), ct);

        if (planned.Count == 0)
            return [];

        var queue = PlannedToCandidates(planned, keywords, lang, brief);

        if (brief is null)
            BackfillQueue(queue, rawNames, keywords, lang, tlds, maxChecks);

        return queue;
    }

    private static string BuildShortAdvice(
        IReadOnlyList<DomainCandidate> ranked,
        int checksUsed,
        int maxChecks,
        int unavailableCount)
    {
        if (ranked.Count == 0)
        {
            return $"No available domains after {checksUsed} of {maxChecks} checks " +
                   $"({unavailableCount} taken or over budget). Try more TLDs or run another search.";
        }

        var top = ranked[0];
        var price = top.PriceUsd is { } p ? $" at ${p:F2}" : "";
        return $"Only one option found: {top.FullDomain}{price}. " +
               $"Checked {checksUsed}/{maxChecks} names — most were taken. Try more TLDs or search again for more coinages.";
    }

    private List<DomainCandidate> PlannedToCandidates(
        IReadOnlyList<PlannedCheck> planned,
        IReadOnlyList<string> keywords,
        string lang,
        SearchBrief? brief = null)
    {
        var list = new List<DomainCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in planned)
        {
            if (!NameSanitizer.IsValidDomainName(item.Label))
                continue;

            if (brief is not null &&
                !DomainQualityFilter.IsAcceptable(item.Label, brief, keywords, useLlm: true))
                continue;

            var key = $"{item.Label}.{item.Tld}";
            if (!seen.Add(key))
                continue;

            var seo = brief is not null
                ? _seoScorer.Score(item.Label, keywords, lang, brief)
                : _seoScorer.Score(item.Label, keywords, lang);

            var seoScore = brief is not null
                ? seo.Score
                : Math.Max(seo.Score, item.Score);

            list.Add(new DomainCandidate
            {
                Name = item.Label,
                Tld = item.Tld,
                SeoScore = seoScore,
                SeoExplanation = seo.Explanation
            });
        }

        return list;
    }

    private void BackfillQueue(
        List<DomainCandidate> queue,
        IReadOnlyList<string> rawNames,
        IReadOnlyList<string> keywords,
        string lang,
        IReadOnlyList<string> tlds,
        int maxChecks)
    {
        if (queue.Count >= maxChecks)
            return;

        var seen = new HashSet<string>(queue.Select(c => c.FullDomain), StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in BuildCandidateQueue(rawNames, keywords, lang, tlds))
        {
            if (queue.Count >= maxChecks)
                break;
            if (seen.Add(candidate.FullDomain))
                queue.Add(candidate);
        }
    }

    private async Task<RefillOutcome?> TryRefillAsync(
        DomainSearchRequest request,
        IReadOnlyList<string> keywords,
        string lang,
        IReadOnlyList<string> tlds,
        int maxChecks,
        List<DomainCandidate> queue,
        List<string> unavailableSample,
        bool refillTriggered,
        List<DomainCandidate> available,
        int checksUsed,
        IProgress<SearchProgressEvent>? progress,
        SearchBrief? brief,
        CancellationToken ct)
    {
        if (refillTriggered ||
            !request.UseLlm ||
            _checkPlanner is null ||
            checksUsed < RefillCheckThreshold ||
            available.Count > 0 ||
            maxChecks - checksUsed < RefillMinRemaining)
        {
            return null;
        }

        Report(progress, "refining", checksUsed, maxChecks, available.Count, null);

        try
        {
            var alreadyChecked = new HashSet<string>(
                queue.Select(c => c.FullDomain),
                StringComparer.OrdinalIgnoreCase);

            var refill = await _checkPlanner.PlanAsync(new CheckPlannerRequest(
                request.Prompt,
                lang,
                keywords,
                tlds,
                maxChecks,
                request.MaxPriceUsd,
                [],
                request.OpenRouterApiKey,
                unavailableSample.ToList(),
                maxChecks - checksUsed,
                DeriveTakenPatternHint(unavailableSample),
                brief), ct);

            foreach (var candidate in PlannedToCandidates(refill, keywords, lang, brief))
            {
                if (alreadyChecked.Add(candidate.FullDomain))
                    queue.Add(candidate);
            }

            return new RefillOutcome(null);
        }
        catch (Exception ex)
        {
            return new RefillOutcome($"AI refill failed: {ex.Message}");
        }
    }

    private static void ApplyRefillOutcome(
        RefillOutcome? outcome,
        CheckLoopState state)
    {
        if (outcome is null)
            return;

        state.RefillTriggered = true;
        if (outcome.WarningAppend is not null)
            state.Warning = AppendWarning(state.Warning, outcome.WarningAppend);
    }

    private async Task<List<DomainCandidate>> TryRecoveryPlannerAsync(
        DomainSearchRequest request,
        IReadOnlyList<string> keywords,
        string lang,
        IReadOnlyList<string> tlds,
        int maxChecks,
        List<DomainCandidate> queue,
        List<string> unavailableSample,
        List<DomainCandidate> available,
        int checksUsed,
        IProgress<SearchProgressEvent>? progress,
        SearchBrief? brief,
        CancellationToken ct)
    {
        if (_checkPlanner is null)
            return [];

        Report(progress, "refining", checksUsed, maxChecks, available.Count, null);

        try
        {
            var alreadyChecked = new HashSet<string>(
                queue.Select(c => c.FullDomain),
                StringComparer.OrdinalIgnoreCase);

            var taken = unavailableSample.ToList();
            foreach (var found in available)
            {
                if (taken.Count >= 12)
                    break;
                if (!taken.Contains(found.FullDomain, StringComparer.OrdinalIgnoreCase))
                    taken.Add(found.FullDomain);
            }

            var remaining = maxChecks - checksUsed;
            var recovery = await _checkPlanner.PlanAsync(new CheckPlannerRequest(
                request.Prompt,
                lang,
                keywords,
                tlds,
                maxChecks,
                request.MaxPriceUsd,
                [],
                request.OpenRouterApiKey,
                taken,
                remaining,
                DeriveTakenPatternHint(taken),
                brief), ct);

            var added = new List<DomainCandidate>();
            foreach (var candidate in PlannedToCandidates(recovery, keywords, lang, brief))
            {
                if (alreadyChecked.Add(candidate.FullDomain))
                {
                    queue.Add(candidate);
                    added.Add(candidate);
                }
            }

            return added;
        }
        catch
        {
            return [];
        }
    }

    private sealed record RefillOutcome(string? WarningAppend);

    internal static string? DeriveTakenPatternHint(IReadOnlyList<string> taken)
    {
        if (taken.Count == 0)
            return null;

        var labels = taken
            .Select(d => d.Split('.', 2)[0])
            .Where(l => l.Length > 0)
            .ToList();

        if (labels.Count == 0)
            return null;

        var suffixHits = labels.Count(l =>
            l.EndsWith("ify", StringComparison.OrdinalIgnoreCase) ||
            l.EndsWith("ly", StringComparison.OrdinalIgnoreCase) ||
            (l.Length >= 5 && l.EndsWith('r')));

        if (suffixHits < (labels.Count + 1) / 2)
            return null;

        return "Taken names heavily used -ify, -ly, or -r suffixes. Use different coined blends (portmanteaus, 6-9 chars).";
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
        decimal maxPriceUsd,
        bool useLlm)
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

        var hint = useLlm
            ? "Try more invented brand names, add TLDs like .app or .dev, or raise the price limit."
            : "Try shorter names, add a country TLD (e.g. .mx), raise the price limit, or enable AI.";

        return AppendWarning(warning,
            $"Checked {checksUsed} domains; none available under ${maxPriceUsd:F0}. {hint}");
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
