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

public sealed class OpenRouterNameGenerator : INameGenerator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenRouterOptions> _options;
    private readonly ILogger<OpenRouterNameGenerator> _logger;

    public OpenRouterNameGenerator(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OpenRouterOptions> options,
        ILogger<OpenRouterNameGenerator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public string Name => "openrouter";

    public async Task<IReadOnlyList<string>> GenerateAsync(DomainSearchRequest request, CancellationToken ct = default)
    {
        var apiKey = request.OpenRouterApiKey ?? _options.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenRouter API key not configured");

        var lang = KeywordExtractor.DetectLanguage(request.Prompt, request.Language);
        var keywords = KeywordExtractor.Extract(request.Prompt, lang);
        var tldList = string.Join(", ", request.Tlds.Select(t => $".{t.Trim().TrimStart('.')}"));

        var systemPrompt = """
            You generate SEO-friendly domain name candidates (domain label only, no TLD).
            Rules: lowercase, no hyphens, no numbers, 5-12 characters, easy to pronounce.
            Prefer short coined brand blends (e.g. pawlynx, walklio) over obvious dictionary phrases likely taken on .com.
            Do not append country codes like mx, us, or uk to the label.
            Combine 1-2 core keywords from the business description for search intent.
            Return ONLY a JSON array of strings, no markdown.
            """;

        var userPrompt = $"""
            Language: {lang}
            Keywords: {string.Join(", ", keywords)}
            Allowed TLDs: {tldList}
            Business description: {request.Prompt}
            Generate {request.MaxCandidates} unique domain name labels optimized for SEO and likely availability.
            """;

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
            temperature = 0.7
        };

        using var response = await client.PostAsJsonAsync("chat/completions", payload, ct);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<OpenRouterChatResponse>(ct)
            ?? throw new InvalidOperationException("Empty OpenRouter response");

        var text = content.Choices?.FirstOrDefault()?.Message?.Content ?? "[]";
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var lines = text.Split('\n').Skip(1);
            text = string.Join('\n', lines).TrimEnd('`', '\n', ' ');
        }

        List<string>? names;
        try
        {
            names = JsonSerializer.Deserialize<List<string>>(text);
        }
        catch
        {
            names = JsonSerializer.Deserialize<OpenRouterNamesWrapper>(text)?.Names;
        }

        return (names ?? [])
            .Select(NameSanitizer.Normalize)
            .Where(NameSanitizer.IsValidDomainName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(request.MaxCandidates * 2)
            .ToList();
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

    private sealed class OpenRouterNamesWrapper
    {
        [JsonPropertyName("names")]
        public List<string>? Names { get; set; }
    }
}
