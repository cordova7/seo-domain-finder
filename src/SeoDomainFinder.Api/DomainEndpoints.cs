using System.Text.Json;
using SeoDomainFinder.Api.Contracts;
using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;
using SeoDomainFinder.Core.Services;
using SeoDomainFinder.Infrastructure.Options;
using SeoDomainFinder.Infrastructure.Porkbun;
using SeoDomainFinder.Infrastructure.RateLimiting;
using Microsoft.Extensions.Options;

namespace SeoDomainFinder.Api;

public static class DomainEndpoints
{
    private static readonly string[] DefaultTlds = ["com", "io"];
    private const int MaxChecksCap = 25;

    public static void MapDomainEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/health", () => Results.Ok(new { status = "healthy", version = "1.0.0" }));

        app.MapPost("/api/v1/domains/search", SearchHandler);

        app.MapPost("/api/v1/domains/search/stream", StreamSearchHandler);
    }

    private static async Task<IResult> SearchHandler(
        DomainSearchDto dto,
        IDomainSearchService searchService,
        PorkbunDomainChecker porkbun,
        DemoRateLimiter rateLimiter,
        IOptionsMonitor<DemoRateLimitOptions> rateOptions,
        HttpContext httpContext,
        ILogger<Program> logger)
    {
        var prepared = TryPrepareSearch(dto, porkbun, rateLimiter, rateOptions, httpContext, out var error);
        if (error is not null)
            return error;

        try
        {
            var result = await searchService.SearchAsync(prepared!.Request, null, httpContext.RequestAborted);
            return Results.Ok(ToResponse(result));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Domain search failed");
            return Results.Problem("Search failed. Please try again.");
        }
    }

    private static async Task StreamSearchHandler(
        DomainSearchDto dto,
        IDomainSearchService searchService,
        PorkbunDomainChecker porkbun,
        DemoRateLimiter rateLimiter,
        IOptionsMonitor<DemoRateLimitOptions> rateOptions,
        HttpContext httpContext,
        ILogger<Program> logger)
    {
        var prepared = TryPrepareSearch(dto, porkbun, rateLimiter, rateOptions, httpContext, out var error);
        if (error is not null)
        {
            await error.ExecuteAsync(httpContext);
            return;
        }

        httpContext.Response.Headers.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        try
        {
            SearchProgressEvent? lastEvent = null;
            var progress = new Progress<SearchProgressEvent>(async evt =>
            {
                lastEvent = evt;
                if (evt.Phase != "done")
                {
                    var payload = JsonSerializer.Serialize(evt, jsonOptions);
                    await httpContext.Response.WriteAsync($"data: {payload}\n\n", httpContext.RequestAborted);
                    await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
                }
            });

            var result = await searchService.SearchAsync(
                prepared!.Request, progress, httpContext.RequestAborted);

            var response = ToResponse(result);
            var done = new
            {
                phase = "done",
                checksUsed = lastEvent?.ChecksUsed ?? 0,
                maxChecks = lastEvent?.MaxChecks ?? prepared.Request.MaxChecks,
                foundCount = response.Candidates.Count,
                currentDomain = (string?)null,
                etaSeconds = 0,
                result = response
            };
            var doneJson = JsonSerializer.Serialize(done, jsonOptions);
            await httpContext.Response.WriteAsync($"data: {doneJson}\n\n", httpContext.RequestAborted);
            await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Streamed domain search failed");
            var err = JsonSerializer.Serialize(new { phase = "error", message = "Search failed." }, jsonOptions);
            await httpContext.Response.WriteAsync($"data: {err}\n\n", httpContext.RequestAborted);
        }
    }

    private static PreparedSearch? TryPrepareSearch(
        DomainSearchDto dto,
        PorkbunDomainChecker porkbun,
        DemoRateLimiter rateLimiter,
        IOptionsMonitor<DemoRateLimitOptions> rateOptions,
        HttpContext httpContext,
        out IResult? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(dto.Prompt) || dto.Prompt.Length > 500)
        {
            error = Results.BadRequest(new { error = "Prompt must be 1-500 characters." });
            return null;
        }

        var sessionId = httpContext.Request.Headers["X-Session-Id"].FirstOrDefault()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? Guid.NewGuid().ToString("N");

        var clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var useLlm = dto.UseLlm;
        var useCustomPorkbun = !string.IsNullOrWhiteSpace(dto.PorkbunApiKey) &&
                               !string.IsNullOrWhiteSpace(dto.PorkbunSecretKey);

        if (useLlm && string.IsNullOrWhiteSpace(dto.OpenRouterApiKey))
        {
            if (!rateLimiter.TryConsumeLlm(clientId, 3, out var llmRemaining))
            {
                error = Results.Json(
                    new { error = "LLM rate limit exceeded. Try again later or disable AI enhancement.", remaining = llmRemaining },
                    statusCode: 429);
                return null;
            }
        }

        var tlds = (dto.Tlds ?? DefaultTlds.ToList())
            .Select(t => t.Trim().TrimStart('.').ToLowerInvariant())
            .Where(NameSanitizer.IsAllowedTld)
            .Distinct()
            .ToList();
        if (tlds.Count == 0) tlds = DefaultTlds.ToList();

        var maxCandidates = Math.Clamp(dto.MaxCandidates ?? 15, 5, 25);
        var sessionLimit = Math.Min(rateOptions.CurrentValue.ChecksPerSession, MaxChecksCap);
        var maxChecks = Math.Clamp(dto.MaxChecks ?? sessionLimit, 10, sessionLimit);

        if (!useCustomPorkbun && !rateLimiter.TryConsumeChecks(sessionId, maxChecks, out var checksRemaining))
        {
            error = Results.Json(
                new { error = "Domain check limit exceeded for this session.", remaining = checksRemaining },
                statusCode: 429);
            return null;
        }

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
            MaxCandidates = maxCandidates,
            MaxChecks = maxChecks
        };

        return new PreparedSearch(request);
    }

    private static DomainSearchResponseDto ToResponse(DomainSearchResult result)
    {
        var availableOnly = result.Candidates
            .Where(c => c.Available == true)
            .Select(c => new DomainCandidateDto(
                c.Name, c.Tld, c.FullDomain, c.SeoScore, c.SeoExplanation,
                c.Available, c.PriceUsd, c.PriceType, c.TotalScore, c.UnavailableReason))
            .ToList();

        return new DomainSearchResponseDto(
            availableOnly,
            result.GeneratorUsed,
            result.ExtractedKeywords,
            result.Warning,
            result.Advice);
    }

    private sealed record PreparedSearch(DomainSearchRequest Request);
}
