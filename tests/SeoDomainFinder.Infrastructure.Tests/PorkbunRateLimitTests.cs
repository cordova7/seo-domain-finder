using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;
using SeoDomainFinder.Infrastructure.Porkbun;

namespace SeoDomainFinder.Infrastructure.Tests;

public class PorkbunRateLimitTests
{
    [Theory]
    [InlineData("rate_limited", true)]
    [InlineData("1 out of 1 checks within 10 seconds used.", true)]
    [InlineData("unavailable", false)]
    public void IsRateLimited_DetectsMessages(string reason, bool expected)
    {
        var result = new DomainCheckResult("test.com", false, null, null, reason);
        Assert.Equal(expected, PorkbunDomainChecker.IsRateLimited(result));
    }

    [Fact]
    public void ParseRetryDelayMs_ReadsTtlFromReason()
    {
        Assert.Equal(10_000, PorkbunDomainChecker.ParseRetryDelayMs("rate_limited|ttl:7|message"));
        Assert.Equal(12_000, PorkbunDomainChecker.ParseRetryDelayMs("rate_limited|ttl:12|message"));
        Assert.Equal(10_000, PorkbunDomainChecker.ParseRetryDelayMs(null));
    }
}
