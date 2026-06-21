using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;
using SeoDomainFinder.Infrastructure.Options;

namespace SeoDomainFinder.Infrastructure.OpenRouter;

public sealed class OpenRouterAdvisor : IDomainAdvisor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenRouterOptions> _options;

    public OpenRouterAdvisor(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OpenRouterOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<string?> AdviseAsync(
        SearchSummary summary,
        string? openRouterApiKey,
        CancellationToken ct = default)
    {
        var apiKey = openRouterApiKey ?? _options.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var found = summary.Found.Count == 0
            ? "none"
            : string.Join(", ", summary.Found.Select(f => $"{f.Domain} (${f.PriceUsd:F2})"));

        var taken = summary.SampleUnavailable.Count == 0
            ? "none sampled"
            : string.Join(", ", summary.SampleUnavailable);

        var userPrompt = $"""
            Business: {summary.Prompt}
            Keywords: {string.Join(", ", summary.Keywords)}
            TLDs searched: {string.Join(", ", summary.Tlds.Select(t => $".{t}"))}
            Max price: ${summary.MaxPriceUsd:F0}
            Checks used: {summary.ChecksUsed}/{summary.MaxChecks}
            Found available: {found}
            Unavailable count: {summary.UnavailableCount}
            Premium skipped: {summary.PremiumSkipped}
            Sample taken names: {taken}
            AI refill used: {(summary.RefillTriggered ? "yes" : "no")}

            Based on this domain search report, pick the best option if any, explain why, and give one concrete next step (naming style or price). Be specific to the data. 3-5 sentences. Plain text only.
            If suggesting TLDs, only mention TLDs from the searched list above — never recommend TLDs the user did not check.
            """;

        var client = _httpClientFactory.CreateClient("OpenRouter");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = _options.CurrentValue.Model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = """
                        You are a domain naming advisor. Be concise, specific, and grounded in the search data provided.
                        Only recommend TLDs from the user's searched list. Never suggest TLDs they did not check.
                        When zero domains were found, suggest retrying with alternate allowed TLDs or more invented brand labels.
                        """
                },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.4
        };

        using var response = await client.PostAsJsonAsync("chat/completions", payload, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadFromJsonAsync<OpenRouterChatResponse>(ct);
        var text = content?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
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
