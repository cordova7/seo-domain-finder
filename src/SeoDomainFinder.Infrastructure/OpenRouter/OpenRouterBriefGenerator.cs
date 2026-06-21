using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;
using SeoDomainFinder.Core.Services;
using SeoDomainFinder.Infrastructure.Options;

namespace SeoDomainFinder.Infrastructure.OpenRouter;

public partial class OpenRouterBriefGenerator : IBriefGenerator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenRouterOptions> _options;
    private readonly ILogger<OpenRouterBriefGenerator> _logger;

    public OpenRouterBriefGenerator(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OpenRouterOptions> options,
        ILogger<OpenRouterBriefGenerator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<SearchBrief> GenerateAsync(BriefGeneratorRequest request, CancellationToken ct = default)
    {
        var lang = KeywordExtractor.DetectLanguage(request.Prompt, request.Language);
        var keywords = KeywordExtractor.Extract(request.Prompt, lang);

        var apiKey = request.OpenRouterApiKey ?? _options.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return SearchBriefFallback.Create(request.Prompt, lang, keywords);

        try
        {
            var tlds = string.Join(", ", request.Tlds.Select(t => $".{t}"));
            var systemPrompt = """
                You interpret a business description for domain name search.
                Respond with a single JSON object:
                {
                  "productSummary": "one sentence what the business is",
                  "audience": "who it serves",
                  "vibe": ["adjective1", "adjective2"],
                  "namingStyles": ["coined 6-9 chars", "portmanteaus"],
                  "conceptKeywords": ["thematic words to evoke, not concatenate"],
                  "avoidTerms": ["trademarks, competitor names, metaphor sources like tinder in tinder-but-for-X"],
                  "avoidPatterns": ["keyword stacks", "-hub", "-app"],
                  "tldStrategy": "when to use each allowed TLD"
                }
                No explanation, no markdown, no text before or after the JSON.
                If the prompt references another product as metaphor (e.g. "X but for Y"), put X in avoidTerms only.
                Capture the interaction model (matching, discovery, swiping, booking) in productSummary and conceptKeywords — not the trademark.
                Do not genericize: preserve subculture, scene, tone, and niche vocabulary in vibe and conceptKeywords.
                conceptKeywords must use words from the user's intent (e.g. hood, street, spar) — not sanitized startup jargon.
                Prefer invented brand names over literal keyword combinations.
                Keep the JSON compact.
                """;

            var userPrompt = $"""
                Language: {lang}
                Business: {request.Prompt}
                Allowed TLDs: {tlds}
                """;

            var text = await CallChatAsync(apiKey, systemPrompt, userPrompt, ct);
            var parsed = ParseBrief(text);
            if (parsed is not null)
                return parsed;

            _logger.LogWarning(
                "Brief parse failed (response length {Length}); using fallback. Snippet: {Snippet}",
                text.Length,
                text.Length > 200 ? text[..200] : text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brief generation failed; using fallback");
        }

        return SearchBriefFallback.Create(request.Prompt, lang, keywords);
    }

    internal static SearchBrief? ParseBrief(string text)
    {
        text = OpenRouterJsonHelper.StripMarkdown(text);
        var objectJson = OpenRouterJsonHelper.ExtractJsonObject(text) ?? text;

        BriefResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<BriefResponse>(objectJson, JsonOptions);
        }
        catch
        {
            parsed = TryParsePartialBrief(objectJson);
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.ProductSummary))
            parsed = TryParsePartialBrief(objectJson);

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.ProductSummary))
            return null;

        var fallbackAvoid = SearchBriefFallback.Create("", "en", []).AvoidTerms;
        var avoidTerms = MergeTerms(parsed.AvoidTerms, fallbackAvoid);

        return new SearchBrief(
            parsed.ProductSummary.Trim(),
            string.IsNullOrWhiteSpace(parsed.Audience) ? "target customers" : parsed.Audience.Trim(),
            NormalizeList(parsed.Vibe, ["memorable"]),
            NormalizeList(parsed.NamingStyles, ["coined 6-9 char brands"]),
            NormalizeList(parsed.ConceptKeywords, []),
            avoidTerms,
            NormalizeList(parsed.AvoidPatterns, ["keyword stacks", "-hub", "-app"]),
            string.IsNullOrWhiteSpace(parsed.TldStrategy)
                ? "prefer .com for main brand"
                : parsed.TldStrategy.Trim());
    }

    internal static BriefResponse? TryParsePartialBrief(string json)
    {
        var productSummary = ExtractStringField(json, "productSummary");
        if (string.IsNullOrWhiteSpace(productSummary))
            return null;

        return new BriefResponse
        {
            ProductSummary = productSummary,
            Audience = ExtractStringField(json, "audience"),
            Vibe = ExtractStringArray(json, "vibe"),
            NamingStyles = ExtractStringArray(json, "namingStyles"),
            ConceptKeywords = ExtractStringArray(json, "conceptKeywords"),
            AvoidTerms = ExtractStringArray(json, "avoidTerms"),
            AvoidPatterns = ExtractStringArray(json, "avoidPatterns"),
            TldStrategy = ExtractStringField(json, "tldStrategy")
        };
    }

    private static string? ExtractStringField(string json, string fieldName)
    {
        foreach (Match m in StringFieldRegex().Matches(json))
        {
            if (string.Equals(m.Groups[1].Value, fieldName, StringComparison.OrdinalIgnoreCase))
                return m.Groups[2].Value;
        }

        return null;
    }

    private static List<string>? ExtractStringArray(string json, string fieldName)
    {
        var pattern = $"\"{fieldName}\"\\s*:\\s*\\[([^\\]]*)\\]";
        var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        var items = ArrayItemRegex().Matches(match.Groups[1].Value)
            .Select(m => m.Groups[1].Value)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return items.Count > 0 ? items : null;
    }

    [GeneratedRegex("\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex StringFieldRegex();

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex ArrayItemRegex();

    private static List<string> MergeTerms(IReadOnlyList<string>? primary, IReadOnlyList<string> defaults)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in defaults)
            set.Add(t);
        if (primary is not null)
        {
            foreach (var t in primary.Where(t => !string.IsNullOrWhiteSpace(t)))
                set.Add(t.Trim());
        }

        return set.ToList();
    }

    private static List<string> NormalizeList(IReadOnlyList<string>? items, string[] defaults) =>
        items is { Count: > 0 }
            ? items.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList()
            : defaults.ToList();

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
            temperature = 0.3,
            max_tokens = 1500,
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

    internal sealed class BriefResponse
    {
        public string? ProductSummary { get; set; }
        public string? Audience { get; set; }
        public List<string>? Vibe { get; set; }
        public List<string>? NamingStyles { get; set; }
        public List<string>? ConceptKeywords { get; set; }
        public List<string>? AvoidTerms { get; set; }
        public List<string>? AvoidPatterns { get; set; }
        public string? TldStrategy { get; set; }
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
