using SeoDomainFinder.Api.Contracts;
using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;
using SeoDomainFinder.Infrastructure.Porkbun;
using SeoDomainFinder.Infrastructure.RateLimiting;

namespace SeoDomainFinder.Api;

public static class DomainEndpoints
{
    private static readonly HashSet<string> DefaultTlds = ["com", "mx", "io", "net"];

    public static void MapDomainEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/health", () => Results.Ok(new { status = "healthy", version = "1.0.0" }));

        app.MapPost("/api/v1/domains/search", async (
            DomainSearchDto dto,
            IDomainSearchService searchService,
            PorkbunDomainChecker porkbun,
            DemoRateLimiter rateLimiter,
            HttpContext httpContext,
            ILogger<Program> logger) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Prompt) || dto.Prompt.Length > 500)
                return Results.BadRequest(new { error = "Prompt must be 1-500 characters." });

            var sessionId = httpContext.Request.Headers["X-Session-Id"].FirstOrDefault()
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? Guid.NewGuid().ToString("N");

            var clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var useLlm = dto.UseLlm;
            var useCustomPorkbun = !string.IsNullOrWhiteSpace(dto.PorkbunApiKey) &&
                                   !string.IsNullOrWhiteSpace(dto.PorkbunSecretKey);

            if (useLlm && string.IsNullOrWhiteSpace(dto.OpenRouterApiKey))
            {
                if (!rateLimiter.TryConsumeLlm(clientId, out var llmRemaining))
                    return Results.Json(new { error = "LLM rate limit exceeded. Try again later or disable AI enhancement.", remaining = llmRemaining }, statusCode: 429);
            }

            var tlds = (dto.Tlds ?? DefaultTlds.ToList())
                .Select(t => t.Trim().TrimStart('.').ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct()
                .ToList();
            if (tlds.Count == 0) tlds = DefaultTlds.ToList();

            var maxCandidates = Math.Clamp(dto.MaxCandidates ?? 15, 5, 25);
            var estimatedChecks = maxCandidates * tlds.Count;

            if (!useCustomPorkbun && !rateLimiter.TryConsumeChecks(sessionId, estimatedChecks, out var checksRemaining))
                return Results.Json(new { error = "Domain check limit exceeded for this session.", remaining = checksRemaining }, statusCode: 429);

            if (useCustomPorkbun)
                porkbun.SetSessionCredentials(sessionId, dto.PorkbunApiKey, dto.PorkbunSecretKey);

            porkbun.CurrentSessionId = useCustomPorkbun ? sessionId : null;

            var request = new DomainSearchRequest
            {
                Prompt = dto.Prompt.Trim(),
                Language = dto.Language,
                Tlds = tlds,
                MaxPriceUsd = dto.MaxPriceUsd ?? 15m,
                UseLlm = useLlm,
                OpenRouterApiKey = dto.OpenRouterApiKey,
                PorkbunApiKey = dto.PorkbunApiKey,
                PorkbunSecretKey = dto.PorkbunSecretKey,
                MaxCandidates = maxCandidates
            };

            try
            {
                var result = await searchService.SearchAsync(request, httpContext.RequestAborted);
                var response = new DomainSearchResponseDto(
                    result.Candidates.Select(c => new DomainCandidateDto(
                        c.Name, c.Tld, c.FullDomain, c.SeoScore, c.SeoExplanation,
                        c.Available, c.PriceUsd, c.PriceType, c.TotalScore, c.UnavailableReason)).ToList(),
                    result.GeneratorUsed,
                    result.ExtractedKeywords,
                    result.Warning);

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Domain search failed");
                return Results.Problem("Search failed. Please try again.");
            }
        });
    }
}
