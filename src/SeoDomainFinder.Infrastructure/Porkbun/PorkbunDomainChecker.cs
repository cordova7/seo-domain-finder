using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Infrastructure.Options;

namespace SeoDomainFinder.Infrastructure.Porkbun;

public sealed class PorkbunDomainChecker : IDomainAvailabilityChecker
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<PorkbunOptions> _options;
    private readonly ILogger<PorkbunDomainChecker> _logger;
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTime _lastCall = DateTime.MinValue;
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
            return new DomainCheckResult(fullDomain, false, null, null, "Porkbun API credentials not configured");
        }

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
                _logger.LogWarning("Porkbun check failed for {Domain}: {Message}", fullDomain, result.Message);
                return new DomainCheckResult(fullDomain, false, null, null, result.Message);
            }

            var available = string.Equals(result.Avail, "yes", StringComparison.OrdinalIgnoreCase);
            decimal? price = decimal.TryParse(result.Price, out var p) ? p : null;
            return new DomainCheckResult(fullDomain, available, price, result.Type,
                available ? null : "unavailable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Porkbun API error for {Domain}", fullDomain);
            return new DomainCheckResult(fullDomain, false, null, null, ex.Message);
        }
    }

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
            var delay = _options.CurrentValue.MinDelayMs -
                        (int)(DateTime.UtcNow - _lastCall).TotalMilliseconds;
            if (delay > 0)
                await Task.Delay(delay, ct);
            _lastCall = DateTime.UtcNow;
        }
        finally
        {
            _throttle.Release();
        }
    }
}
