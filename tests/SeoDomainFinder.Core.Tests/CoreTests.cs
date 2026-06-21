using SeoDomainFinder.Core.Scoring;
using SeoDomainFinder.Core.Services;

namespace SeoDomainFinder.Core.Tests;

public class KeywordExtractorTests
{
    [Fact]
    public void Extract_FiltersStopWords()
    {
        var keywords = KeywordExtractor.Extract(
            "I want to build a judicial alert monitoring app for lawyers in Mexico",
            "en");

        Assert.Contains(keywords, k => k.Contains("judicial", StringComparison.OrdinalIgnoreCase) ||
                                       k.Contains("alert", StringComparison.OrdinalIgnoreCase) ||
                                       k.Contains("lawyer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keywords, k => k.Equals("want", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DetectLanguage_Spanish()
    {
        var lang = KeywordExtractor.DetectLanguage("alertas judiciales para abogados en México", null);
        Assert.Equal("es", lang);
    }
}

public class NameSanitizerTests
{
    [Theory]
    [InlineData("alertasjudmx", true)]
    [InlineData("my-domain", false)]
    [InlineData("domain123", false)]
    [InlineData("ab", false)]
    public void IsValidDomainName_Works(string name, bool expected)
    {
        Assert.Equal(expected, NameSanitizer.IsValidDomainName(name));
    }

    [Fact]
    public void Normalize_RemovesInvalidChars()
    {
        Assert.Equal("alertasjud", NameSanitizer.Normalize("Alertas-Jud!!!"));
    }
}

public class SeoScorerTests
{
    [Fact]
    public void Score_MatchesKeywords()
    {
        var scorer = new SeoScorer();
        var result = scorer.Score("alertasjudmx", ["alertas", "judicial", "mexico"], "es");

        Assert.True(result.Score > 0);
        Assert.Contains("alertas", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }
}

public class HeuristicNameGeneratorTests
{
    [Fact]
    public async Task Generate_ReturnsCandidates()
    {
        var gen = new Core.Generators.HeuristicNameGenerator();
        var names = await gen.GenerateAsync(new Core.Models.DomainSearchRequest
        {
            Prompt = "judicial alert monitoring for lawyers",
            Language = "en",
            MaxCandidates = 10
        });

        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.True(NameSanitizer.IsValidDomainName(n)));
    }
}
