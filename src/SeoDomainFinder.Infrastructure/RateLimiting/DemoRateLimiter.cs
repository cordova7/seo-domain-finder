using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SeoDomainFinder.Infrastructure.Options;

namespace SeoDomainFinder.Infrastructure.RateLimiting;

public sealed class DemoRateLimiter
{
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<DemoRateLimitOptions> _options;

    public DemoRateLimiter(IMemoryCache cache, IOptionsMonitor<DemoRateLimitOptions> options)
    {
        _cache = cache;
        _options = options;
    }

    public bool TryConsumeLlm(string clientId, out int remaining) =>
        TryConsumeLlm(clientId, 1, out remaining);

    public bool TryConsumeLlm(string clientId, int count, out int remaining)
    {
        var key = $"llm:{clientId}:{DateTime.UtcNow:yyyyMMddHH}";
        var used = _cache.GetOrCreate(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return 0;
        });
        var limit = _options.CurrentValue.LlmPerHour;
        remaining = Math.Max(0, limit - used);
        if (used + count > limit) return false;
        _cache.Set(key, used + count, TimeSpan.FromHours(1));
        remaining = Math.Max(0, limit - used - count);
        return true;
    }

    public bool TryConsumeChecks(string sessionId, int count, out int remaining)
    {
        var key = $"checks:{sessionId}";
        var used = _cache.GetOrCreate(key, e =>
        {
            e.SlidingExpiration = TimeSpan.FromHours(24);
            return 0;
        });
        var limit = _options.CurrentValue.ChecksPerSession;
        remaining = Math.Max(0, limit - used);
        if (used + count > limit) return false;
        _cache.Set(key, used + count, TimeSpan.FromHours(24));
        remaining = Math.Max(0, limit - used - count);
        return true;
    }
}
