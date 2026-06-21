using SeoDomainFinder.Infrastructure.OpenRouter;

namespace SeoDomainFinder.Infrastructure.Tests;

public class CheckPlannerParseTests
{
    [Fact]
    public void BuildSystemPrompt_IncludesTieredNamingAndAvailabilityRanking()
    {
        var prompt = OpenRouterCheckPlanner.BuildSystemPrompt(isRefill: false, isTopUp: false, takenPatternHint: null);

        Assert.Contains("soft metaphor", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fadcrate", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("omit anything below 70", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyCheckCountPlaceholder_DoesNotCorruptJson()
    {
        var prompt = OpenRouterCheckPlanner.ApplyCheckCountPlaceholder(
            OpenRouterCheckPlanner.BuildSystemPrompt(isRefill: false, isTopUp: false, takenPatternHint: null),
            12);

        Assert.Contains("JSON", prompt);
        Assert.Contains("No explanation", prompt);
        Assert.DoesNotContain("JSO12", prompt);
        Assert.DoesNotContain("12o explanation", prompt);
        Assert.Contains("12 checks", prompt);
    }

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

    [Fact]
    public void ParseChecks_GarbledPrefixFromFreeModel()
    {
        var text = """
            O25LY JSO25: { "checks": [
              { "label": "fightdate", "tld": "com", "score": 88 },
              { "label": "fightmatch", "tld": "com", "score": 85 },
              { "label": "fightlink", "tld": "net", "score": 55 }
            ] }
            """;

        var checks = OpenRouterCheckPlanner.ParseChecks(text, ["com", "io"], 25);

        Assert.Equal(2, checks.Count);
        Assert.Equal("fightdate", checks[0].Label);
        Assert.Equal("com", checks[0].Tld);
        Assert.Equal("fightmatch", checks[1].Label);
        Assert.DoesNotContain(checks, c => c.Tld == "net");
    }

    [Fact]
    public void ParseChecks_FiltersMashupsWhenBriefPresent()
    {
        var brief = new SeoDomainFinder.Core.Models.SearchBrief(
            "Fighter matching app",
            "fighters",
            ["gritty"],
            ["coined brands"],
            ["hood", "street", "fight"],
            ["tinder"],
            ["keyword stacks"],
            ".io for apps");

        var json = """
            {
              "checks": [
                { "label": "brawlr", "tld": "io", "score": 90 },
                { "label": "tinderhoodstreet", "tld": "com", "score": 85 },
                { "label": "tinderapp", "tld": "com", "score": 80 }
              ]
            }
            """;

        var checks = OpenRouterCheckPlanner.ParseChecks(
            json, ["com", "io"], 25, brief, ["tinder", "hood", "street", "fighters"]);

        Assert.Single(checks);
        Assert.Equal("brawlr", checks[0].Label);
    }
}

public class NameGeneratorParseTests
{
    [Fact]
    public void ParseNames_GarbledPrefixBeforeArray()
    {
        var text = """Here are names: ["pawlynx", "walklio", "ab"]""";

        var names = OpenRouterNameGenerator.ParseNames(text, 10);

        Assert.Equal(2, names.Count);
        Assert.Contains("pawlynx", names);
        Assert.Contains("walklio", names);
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
