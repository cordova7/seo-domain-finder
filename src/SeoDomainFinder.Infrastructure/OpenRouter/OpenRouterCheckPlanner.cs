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
                Previous names were taken. Generate NEW coined domains only.
                Respond with a single JSON object: { "checks": [ { "label": "sparqo", "tld": "io", "score": 88 } ] }
                No explanation, no markdown, no text before or after the JSON.
                Rules: lowercase labels, no hyphens/numbers, 6-8 chars, one TLD per label.
                Opaque portmanteaus only — never copy taken names or their patterns.
                NEVER use fight/batt/clash/punch prefixes or -ix/-ly/-ify/-hub suffixes.
                If multiple TLDs are allowed, use each roughly equally (no more than half on .com).
                Return up to {CHECK_COUNT} checks in the checks array (at least 8 if possible).
                """;
        }
        else
        {
            systemPrompt = """
                You plan which domains to availability-check for a business.
                Respond with a single JSON object: { "checks": [ { "label": "sparqo", "tld": "io", "score": 92 }, { "label": "brawlr", "tld": "com", "score": 90 } ] }
                No explanation, no markdown, no text before or after the JSON.
                Rules: lowercase labels, no hyphens/numbers, 6-8 chars, one TLD per label.
                Coined pronounceable brands only — not dictionary words with startup suffixes.
                NEVER use fight/batt/clash/punch prefixes or -ix/-ly/-ify/-hub suffixes.
                If multiple TLDs are allowed, use each roughly equally (no more than half on .com).
                Rank best first. Return up to {CHECK_COUNT} checks in the checks array (at least 12 if possible).
                """;
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
            {(request.Tlds.Count > 1 ? "Distribute checks evenly across allowed TLDs — do not put more than half on .com." : "")}
            Max price USD: {request.MaxPriceUsd:F0}
            Check budget: {checkBudget}
            Taken/unavailable so far: {taken}
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
