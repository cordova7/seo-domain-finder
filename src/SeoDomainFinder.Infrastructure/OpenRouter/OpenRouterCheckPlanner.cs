using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Services;
using SeoDomainFinder.Infrastructure.Options;

namespace SeoDomainFinder.Infrastructure.OpenRouter;

public sealed class OpenRouterCheckPlanner : ICheckPlanner
{
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

        var isRefill = request.TakenSample is { Count: > 0 };
        var checkBudget = request.RemainingChecks ?? request.MaxChecks;

        var systemPrompt = isRefill
            ? """
              You replan domain availability checks after many names were taken.
              Respond with a single JSON object: { "checks": [ { "label": "pawlynx", "tld": "com", "score": 88 } ] }
              No explanation, no markdown, no text before or after the JSON.
              Rules: lowercase labels, no hyphens/numbers, 5-12 chars, one TLD per label.
              Avoid patterns similar to taken names. Prefer coined brand names over dictionary phrases.
              At most N checks. Pick TLDs most likely free under the price cap.
              """
            : """
              You plan which domains to availability-check for a business.
              Respond with a single JSON object: { "checks": [ { "label": "dogdrift", "tld": "com", "score": 92 } ] }
              No explanation, no markdown, no text before or after the JSON.
              Rules: lowercase labels, no hyphens/numbers, 5-12 chars, one TLD per label.
              Prefer coined/blended names over obvious keyword combos (likely taken on .com).
              Usually pick .com for global/US businesses unless another TLD fits better.
              Rank best first. At most N checks total.
              """;

        var seeds = string.Join(", ", request.SeedNames.Take(40));
        var keywords = string.Join(", ", request.Keywords);
        var tlds = string.Join(", ", request.Tlds.Select(t => $".{t}"));
        var taken = request.TakenSample is { Count: > 0 }
            ? string.Join(", ", request.TakenSample.Take(12))
            : "none yet";

        var userPrompt = $"""
            Language: {request.Language}
            Business: {request.Prompt}
            Keywords: {keywords}
            Allowed TLDs: {tlds}
            Max price USD: {request.MaxPriceUsd:F0}
            Check budget: {checkBudget}
            Name seeds (ideas): {seeds}
            Taken/unavailable so far: {taken}
            """;

        var text = await CallChatAsync(apiKey, systemPrompt.Replace("N", checkBudget.ToString()), userPrompt, ct);
        return ParseChecks(text, request.Tlds, checkBudget);
    }

    internal static IReadOnlyList<PlannedCheck> ParseChecks(
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
                    return FilterChecks(arr, allowedTlds, maxChecks);
            }
            catch
            {
                // fall through
            }
        }

        var objectJson = OpenRouterJsonHelper.ExtractJsonObject(text) ?? text;

        PlannerResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlannerResponse>(objectJson, JsonOptions);
        }
        catch
        {
            return [];
        }

        if (parsed?.Checks is null || parsed.Checks.Count == 0)
            return [];

        return FilterChecks(parsed.Checks, allowedTlds, maxChecks);
    }

    private static List<PlannedCheck> FilterChecks(
        List<PlannerCheck> items,
        IReadOnlyList<string> allowedTlds,
        int maxChecks)
    {
        var allowed = new HashSet<string>(
            allowedTlds.Select(t => t.Trim().TrimStart('.').ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PlannedCheck>();

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

    private sealed class PlannerCheck
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
