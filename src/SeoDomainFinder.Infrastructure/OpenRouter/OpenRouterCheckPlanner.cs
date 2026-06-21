using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;
using SeoDomainFinder.Core.Services;
using SeoDomainFinder.Infrastructure.Options;

namespace SeoDomainFinder.Infrastructure.OpenRouter;

public sealed class OpenRouterCheckPlanner : ICheckPlanner
{
    internal const string CheckCountPlaceholder = "{CHECK_COUNT}";

    internal const string AvailabilitySelfCheck = """
        AVAILABILITY RANKING (apply to every candidate — still return the full check budget):
        1. NEVER use -ify, -ly, -ix, -hub, or dictionary+suffix patterns (bookify, shoply, linkify).
        2. Short 6-7 char pronounceable .com names rank LOW — include at most 1-2 as longshots, not the whole batch.
        3. Prefer names squatters would skip: 8-11 char soft metaphors (fadcrate, buzzstall) or opaque blends (sparqova).
        4. Skip near-duplicates of taken/unavailable names already listed.
        score = combined rank: availability guess (higher=likelier free) + brand quality. Sort checks best-first.
        """;

    internal const string TieredNamingGuide = """
        Fill the batch with a MIX (not all one style):
        - About 40% soft metaphor portmanteaus: two morphemes that evoke the concept (examples: fadcrate, buzzparcel, hoodspar, virstall) — 8-11 chars, pronounceable, NO -ify/-ly.
        - About 60% opaque coined blends: 8-10 chars with uncommon letters (q, v, z, k), one soft concept hit.
        BAD (almost always taken): bookify, linkly, viralstore, shopapp, 6-letter trendy .com.
        GOOD (often free): fadcrate, sparqova, hoodlynx, buzznook, vircart.
        """;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenRouterOptions> _options;
    private readonly ILogger<OpenRouterCheckPlanner> _logger;

    public OpenRouterCheckPlanner(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OpenRouterOptions> options,
        ILogger<OpenRouterCheckPlanner> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlannedCheck>> PlanAsync(
        CheckPlannerRequest request,
        CancellationToken ct = default)
    {
        var apiKey = request.OpenRouterApiKey ?? _options.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenRouter API key not configured");

        var isRefill = request.TakenSample is { Count: > 0 } && !request.IsTopUp;
        var isTopUp = request.IsTopUp;
        var hasBrief = request.Brief is not null;

        var checkBudget = request.RemainingChecks ?? request.MaxChecks;

        var systemPrompt = ApplyCheckCountPlaceholder(
            BuildSystemPrompt(isRefill, isTopUp, request.TakenPatternHint),
            checkBudget);

        var userPrompt = hasBrief
            ? BuildBriefUserPrompt(request, checkBudget)
            : BuildLegacyUserPrompt(request, checkBudget);

        var text = await CallChatAsync(apiKey, systemPrompt, userPrompt, ct);
        var raw = ParseChecksRaw(text, request.Tlds, checkBudget);
        var filtered = FilterChecks(
            raw,
            request.Tlds,
            checkBudget,
            request.Brief,
            request.Keywords);

        if (raw.Count == 0)
        {
            _logger.LogWarning(
                "Planner parsed 0 checks from model response (length {Length}): {Snippet}",
                text.Length,
                text.Length > 200 ? text[..200] : text);
        }
        else if (filtered.Count == 0)
        {
            _logger.LogWarning(
                "Planner filtered all {RawCount} parsed checks (quality gate rejected every label)",
                raw.Count);
        }
        else if (filtered.Count < checkBudget)
        {
            _logger.LogDebug(
                "Planner returned {Count}/{Budget} checks after quality filter",
                filtered.Count,
                checkBudget);
        }

        return filtered;
    }

    internal static string ApplyCheckCountPlaceholder(string systemPrompt, int checkBudget) =>
        systemPrompt.Replace(CheckCountPlaceholder, checkBudget.ToString(), StringComparison.Ordinal);

    internal static string BuildSystemPrompt(bool isRefill, bool isTopUp, string? takenPatternHint)
    {
        string systemPrompt;
        if (isTopUp)
        {
            systemPrompt = """
                Generate MORE coined domain names different from the prior batch.
                Respond with a single JSON object: { "checks": [ { "label": "brawlr", "tld": "io", "score": 88 } ] }
                No explanation, no markdown, no text before or after the JSON.
                Rules: lowercase labels, no hyphens/numbers, 5-12 chars, one TLD per label.
                Prefer invented brand names. Never concatenate multiple keywords.
                Return up to {CHECK_COUNT} checks in the checks array (at least 8 if possible).
                """;
        }
        else if (isRefill)
        {
            systemPrompt = """
                Batch 1 names were taken. Generate NEW coined domains that still evoke the same conceptKeywords for SEO.
                Respond with a single JSON object: { "checks": [ { "label": "sparqo", "tld": "io", "score": 88 } ] }
                No explanation, no markdown, no text before or after the JSON.
                Rules: lowercase labels, no hyphens/numbers, 8-10 chars preferred, one TLD per label.
                Use opaque portmanteaus with one soft concept hit — not keyword stacks or taken labels.
                Avoid roots and patterns listed in the taken hint. Do not reuse taken labels or near-miss spellings.
                NEVER use fight/batt/clash/punch prefixes or -ix/-ly/-ify/-hub suffixes.
                If multiple TLDs are allowed, use each roughly equally (no more than half on .com).
                Return up to {CHECK_COUNT} checks in the checks array (at least 8 if possible).
                """ + "\n" + TieredNamingGuide + "\n" + AvailabilitySelfCheck;
        }
        else
        {
            systemPrompt = """
                You plan which domains to availability-check for a business.
                Respond with a single JSON object: { "checks": [ { "label": "fadcrate", "tld": "com", "score": 92 }, { "label": "sparqova", "tld": "io", "score": 88 } ] }
                No explanation, no markdown, no text before or after the JSON.
                Rules: lowercase labels, no hyphens/numbers, 8-11 chars preferred (7 min), one TLD per label.
                Coined brands for SEO + registrability — evoke ONE conceptKeyword, never concatenate two full keywords.
                NEVER use fight/batt/clash/punch prefixes or -ix/-ly/-ify/-hub suffixes.
                If multiple TLDs are allowed, use each roughly equally (no more than half on .com).
                Rank by brand quality + likelihood of being free. Return exactly up to {CHECK_COUNT} checks.
                """ + "\n" + TieredNamingGuide + "\n" + AvailabilitySelfCheck;
        }

        if ((isRefill || isTopUp) && !string.IsNullOrWhiteSpace(takenPatternHint))
            systemPrompt += "\n" + takenPatternHint;

        return systemPrompt;
    }

    private static string BuildBriefUserPrompt(CheckPlannerRequest request, int checkBudget)
    {
        var brief = request.Brief!;
        var tlds = string.Join(", ", request.Tlds.Select(t => $".{t}"));
        var taken = request.TakenSample is { Count: > 0 }
            ? string.Join(", ", request.TakenSample.Take(12))
            : "none yet";

        return $"""
            Original user prompt: {request.Prompt}
            Product: {brief.ProductSummary}
            Audience: {brief.Audience}
            Vibe: {string.Join(", ", brief.Vibe)}
            Naming styles: {string.Join(", ", brief.NamingStyles)}
            Concept keywords (evoke, do not concatenate): {string.Join(", ", brief.ConceptKeywords)}
            NEVER use these terms: {string.Join(", ", brief.AvoidTerms)}
            NEVER use these patterns: {string.Join(", ", brief.AvoidPatterns)}
            TLD strategy: {brief.TldStrategy}
            Allowed TLDs: {tlds}
            {(request.Tlds.Count == 1 && request.Tlds[0].Equals("com", StringComparison.OrdinalIgnoreCase)
                ? "Only .com allowed — use soft metaphors (fadcrate, buzzstall) and 8-11 char coinages; avoid 6-letter trendy .com."
                : request.Tlds.Count > 1
                    ? "Distribute checks evenly across allowed TLDs — do not put more than half on .com."
                    : "")}
            Max price USD: {request.MaxPriceUsd:F0}
            Check budget: {checkBudget}
            Taken/unavailable so far: {taken}
            Use the tiered mix: soft metaphor portmanteaus + opaque blends. Never -ify/-ly suffix spam.
            """;
    }

    private static string BuildLegacyUserPrompt(CheckPlannerRequest request, int checkBudget)
    {
        var seeds = string.Join(", ", request.SeedNames.Take(40));
        var keywords = string.Join(", ", request.Keywords);
        var tlds = string.Join(", ", request.Tlds.Select(t => $".{t}"));
        var taken = request.TakenSample is { Count: > 0 }
            ? string.Join(", ", request.TakenSample.Take(12))
            : "none yet";

        return $"""
            Language: {request.Language}
            Business: {request.Prompt}
            Keywords: {keywords}
            Allowed TLDs: {tlds}
            Max price USD: {request.MaxPriceUsd:F0}
            Check budget: {checkBudget}
            Name seeds (ideas): {seeds}
            Taken/unavailable so far: {taken}
            """;
    }

    internal static IReadOnlyList<PlannedCheck> ParseChecks(
        string text,
        IReadOnlyList<string> allowedTlds,
        int maxChecks,
        SearchBrief? brief = null,
        IReadOnlyList<string>? keywords = null) =>
        FilterChecks(ParseChecksRaw(text, allowedTlds, maxChecks), allowedTlds, maxChecks, brief, keywords);

    private static List<PlannerCheck> ParseChecksRaw(
        string text,
        IReadOnlyList<string> allowedTlds,
        int maxChecks)
    {
        text = OpenRouterJsonHelper.StripMarkdown(text);

        if (text.StartsWith('['))
        {
            var arrayJson = OpenRouterJsonHelper.ExtractJsonArray(text) ?? text;
            try
            {
                var arr = JsonSerializer.Deserialize<List<PlannerCheck>>(arrayJson, JsonOptions);
                if (arr is { Count: > 0 })
                    return arr;
            }
            catch
            {
                // fall through
            }
        }

        var objectJson = OpenRouterJsonHelper.ExtractJsonObject(text) ?? text;

        try
        {
            var parsed = JsonSerializer.Deserialize<PlannerResponse>(objectJson, JsonOptions);
            if (parsed?.Checks is { Count: > 0 })
                return parsed.Checks;
        }
        catch
        {
            // fall through
        }

        return [];
    }

    internal static List<PlannedCheck> FilterChecks(
        List<PlannerCheck> items,
        IReadOnlyList<string> allowedTlds,
        int maxChecks,
        SearchBrief? brief,
        IReadOnlyList<string>? keywords)
    {
        var allowed = new HashSet<string>(
            allowedTlds.Select(t => t.Trim().TrimStart('.').ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PlannedCheck>();
        var kw = keywords ?? [];

        foreach (var item in items)
        {
            if (result.Count >= maxChecks)
                break;

            var label = NameSanitizer.Normalize(item.Label ?? item.Domain ?? "");
            var tld = (item.Tld ?? "").Trim().TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(tld) && label.Contains('.'))
            {
                var parts = label.Split('.', 2);
                label = NameSanitizer.Normalize(parts[0]);
                tld = parts[1].ToLowerInvariant();
            }

            if (!NameSanitizer.IsValidDomainName(label) || !allowed.Contains(tld))
                continue;

            if (brief is not null && !DomainQualityFilter.IsAcceptable(label, brief, kw, useLlm: true))
                continue;

            var key = $"{label}.{tld}";
            if (!seen.Add(key))
                continue;

            result.Add(new PlannedCheck(label, tld, Math.Clamp(item.Score, 0, 100)));
        }

        return result;
    }

    private async Task<string> CallChatAsync(
        string apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OpenRouter");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = _options.CurrentValue.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.2,
            max_tokens = 2000,
            response_format = new { type = "json_object" }
        };

        using var response = await client.PostAsJsonAsync("chat/completions", payload, ct);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<OpenRouterChatResponse>(ct)
            ?? throw new InvalidOperationException("Empty OpenRouter response");

        return content.Choices?.FirstOrDefault()?.Message?.Content ?? "{}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class PlannerResponse
    {
        public List<PlannerCheck>? Checks { get; set; }
    }

    internal sealed class PlannerCheck
    {
        public string? Label { get; set; }
        public string? Domain { get; set; }
        public string? Tld { get; set; }
        public int Score { get; set; }
    }

    private sealed class OpenRouterChatResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenRouterChoice>? Choices { get; set; }
    }

    private sealed class OpenRouterChoice
    {
        [JsonPropertyName("message")]
        public OpenRouterMessage? Message { get; set; }
    }

    private sealed class OpenRouterMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
