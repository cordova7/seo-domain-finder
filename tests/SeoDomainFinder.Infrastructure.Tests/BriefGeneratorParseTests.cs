using SeoDomainFinder.Infrastructure.OpenRouter;

namespace SeoDomainFinder.Infrastructure.Tests;

public class BriefGeneratorParseTests
{
    [Fact]
    public void ParseBrief_ValidJson()
    {
        var json = """
            {
              "productSummary": "Social app for urban fighters",
              "audience": "street fighters",
              "vibe": ["gritty", "urban"],
              "namingStyles": ["coined 6-9 chars"],
              "conceptKeywords": ["fight", "hood", "match"],
              "avoidTerms": ["tinder"],
              "avoidPatterns": ["keyword stacks"],
              "tldStrategy": "prefer .io for apps"
            }
            """;

        var brief = OpenRouterBriefGenerator.ParseBrief(json);

        Assert.NotNull(brief);
        Assert.Contains("fighters", brief.ProductSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(brief.AvoidTerms, t => t.Equals("tinder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseBrief_GarbledPrefixFallsBackToExtract()
    {
        var text = """
            Some reasoning here...
            { "productSummary": "Dating for fighters", "audience": "fighters",
              "vibe": ["bold"], "namingStyles": ["portmanteaus"],
              "conceptKeywords": ["brawl"], "avoidTerms": ["tinder"],
              "avoidPatterns": ["-hub"], "tldStrategy": ".com first" }
            """;

        var brief = OpenRouterBriefGenerator.ParseBrief(text);

        Assert.NotNull(brief);
        Assert.Equal("Dating for fighters", brief.ProductSummary);
    }
}
