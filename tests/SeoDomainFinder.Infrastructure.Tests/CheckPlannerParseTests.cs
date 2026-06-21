using SeoDomainFinder.Infrastructure.OpenRouter;

namespace SeoDomainFinder.Infrastructure.Tests;

public class CheckPlannerParseTests
{
    [Fact]
    public void ParseChecks_ValidJson()
    {
        var json = """
            {
              "checks": [
                { "label": "dogdrift", "tld": "com", "score": 90 },
                { "label": "bad-name", "tld": "com", "score": 80 },
                { "label": "pawlynx", "tld": "xyz", "score": 70 }
              ]
            }
            """;

        var checks = OpenRouterCheckPlanner.ParseChecks(json, ["com", "io"], 25);

        Assert.Equal(2, checks.Count);
        Assert.Equal("dogdrift", checks[0].Label);
        Assert.Equal("com", checks[0].Tld);
    }
}

public class PorkbunPremiumTests
{
    [Fact]
    public void InnerResponse_DetectsPremiumYes()
    {
        var inner = new SeoDomainFinder.Infrastructure.Porkbun.PorkbunCheckInnerResponse
        {
            Premium = "yes"
        };
        Assert.True(inner.IsPremium);
    }
}
