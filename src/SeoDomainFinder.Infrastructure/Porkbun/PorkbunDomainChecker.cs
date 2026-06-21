using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;
using SeoDomainFinder.Infrastructure.Options;

namespace SeoDomainFinder.Infrastructure.Porkbun;

public sealed class PorkbunDomainChecker : IDomainAvailabilityChecker
{
    private const int MaxRateLimitRetries = 3;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<PorkbunOptions> _options;
    private readonly ILogger<PorkbunDomainChecker> _logger;
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTime _nextAllowedCallUtc = DateTime.MinValue;
    private readonly ConcurrentDictionary<string, (string? ApiKey, string? Secret)> _sessionCredentials = new();

    public PorkbunDomainChecker(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<PorkbunOptions> options,
        ILogger<PorkbunDomainChecker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public void SetSessionCredentials(string sessionId, string? apiKey, string? secret)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secret))
            _sessionCredentials.TryRemove(sessionId, out _);
        else
            _sessionCredentials[sessionId] = (apiKey, secret);
    }

    public string? CurrentSessionId { get; set; }

    public async Task<DomainCheckResult> CheckAsync(string fullDomain, CancellationToken ct = default)
    {
        var (apiKey, secret) = ResolveCredentials();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secret))
        {
            return new DomainCheckResult(
                fullDomain, false, null, null, DomainCheckReasons.CredentialsMissing);
        }

        DomainCheckResult? lastRateLimited = null;
        for (var attempt = 0; attempt <= MaxRateLimitRetries; attempt++)
        {
            var result = await CheckOnceAsync(fullDomain, apiKey, secret, ct);
            if (!IsRateLimited(result))
                return result;

            lastRateLimited = result;
            if (attempt == MaxRateLimitRetries)
                break;

            var waitMs = ParseRetryDelayMs(result.Reason);
            _logger.LogInformation(
                "Porkbun rate limit for {Domain}, waiting {WaitMs}ms (attempt {Attempt})",
                fullDomain, waitMs, attempt + 1);
            _nextAllowedCallUtc = DateTime.UtcNow.AddMilliseconds(waitMs);
            await Task.Delay(waitMs, ct);
        }

        return lastRateLimited ?? new DomainCheckResult(
            fullDomain, false, null, null, DomainCheckReasons.RateLimited);
    }

    public static bool IsRateLimited(DomainCheckResult result) =>
        string.Equals(result.Reason, DomainCheckReasons.RateLimited, StringComparison.OrdinalIgnoreCase) ||
        (result.Reason?.Contains("within 10 seconds", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (result.Reason?.Contains("RATE_LIMIT", StringComparison.OrdinalIgnoreCase) ?? false);

    public static bool IsPremium(DomainCheckResult result) =>
        string.Equals(result.Reason, DomainCheckReasons.Premium, StringComparison.OrdinalIgnoreCase);

    public static int ParseRetryDelayMs(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return 10_000;

        var match = System.Text.RegularExpressions.Regex.Match(reason, @"ttl:(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var ttl) && ttl > 0)
            return Math.Max(10_000, ttl * 1000);

        return 10_000;
    }

    private async Task<DomainCheckResult> CheckOnceAsync(
        string fullDomain, string apiKey, string secret, CancellationToken ct)
    {
        await ThrottleAsync(ct);

        var client = _httpClientFactory.CreateClient("Porkbun");
        var body = new { apikey = apiKey, secretapikey = secret };
        try
        {
            using var response = await client.PostAsJsonAsync($"domain/checkDomain/{fullDomain}", body, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<PorkbunCheckResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result is null)
                return new DomainCheckResult(fullDomain, false, null, null, "Invalid Porkbun response");

            if (!string.Equals(result.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                var message = result.Message;
                if (string.IsNullOrWhiteSpace(message))
                    message = $"HTTP {(int)response.StatusCode}";

                if (IsRateLimitMessage(message))
                {
                    var ttl = result.TtlRemaining ?? 10;
                    ScheduleNextCall(ttl);
                    return new DomainCheckResult(
                        fullDomain, false, null, null, $"rate_limited|ttl:{ttl}|{message}");
                }

                _logger.LogWarning("Porkbun check failed for {Domain}: {Message}", fullDomain, message);
                return new DomainCheckResult(fullDomain, false, null, null, message);
            }

            var inner = result.Response;
            var avail = inner?.Avail ?? result.Avail;
            var priceStr = inner?.Price ?? result.Price;
            var type = inner?.Type ?? result.Type;
            var isPremium = inner?.IsPremium == true;
            var ttlRemaining = result.TtlRemaining;
            ScheduleNextCall(ttlRemaining);

            if (isPremium)
            {
                return new DomainCheckResult(
                    fullDomain, false, null, type, DomainCheckReasons.Premium);
            }

            var available = string.Equals(avail, "yes", StringComparison.OrdinalIgnoreCase);
            decimal? price = decimal.TryParse(priceStr, out var p) ? p : null;
            return new DomainCheckResult(
                fullDomain,
                available,
                price,
                type,
                available ? null : DomainCheckReasons.Unavailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Porkbun API error for {Domain}", fullDomain);
            return new DomainCheckResult(fullDomain, false, null, null, ex.Message);
        }
    }

    private void ScheduleNextCall(int? ttlRemainingSeconds)
    {
        var minDelay = _options.CurrentValue.MinDelayMs;
        var delayMs = ttlRemainingSeconds is > 0
            ? Math.Max(minDelay, ttlRemainingSeconds.Value * 1000)
            : minDelay;
        _nextAllowedCallUtc = DateTime.UtcNow.AddMilliseconds(delayMs);
    }

    private static bool IsRateLimitMessage(string message) =>
        message.Contains("within 10 seconds", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("RATE_LIMIT", StringComparison.OrdinalIgnoreCase);

    private (string? ApiKey, string? Secret) ResolveCredentials()
    {
        if (!string.IsNullOrWhiteSpace(CurrentSessionId) &&
            _sessionCredentials.TryGetValue(CurrentSessionId, out var session))
            return session;

        var opts = _options.CurrentValue;
        return (opts.ApiKey, opts.SecretKey);
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var delay = (int)(_nextAllowedCallUtc - DateTime.UtcNow).TotalMilliseconds;
            if (delay > 0)
                await Task.Delay(delay, ct);
        }
        finally
        {
            _throttle.Release();
        }
    }
}
