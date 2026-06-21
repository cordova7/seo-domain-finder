using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Scoring;

public sealed class SeoScorer : ISeoScorer
{
    private static readonly string[] ModifierSuffixes =
        ["app", "hub", "pro", "now", "auto", "online", "digital", "cloud"];

    public SeoScoreResult Score(string domainName, IReadOnlyList<string> keywords, string? language) =>
        Score(domainName, keywords, language, null);

    public SeoScoreResult Score(
        string domainName,
        IReadOnlyList<string> keywords,
        string? language,
        SearchBrief? brief)
    {
        if (brief is null)
            return ScoreClassic(domainName, keywords);

        return ScoreWithBrief(domainName, keywords, brief);
    }

    private static SeoScoreResult ScoreClassic(string domainName, IReadOnlyList<string> keywords)
    {
        var name = domainName.ToLowerInvariant();
        var score = 0;
        var parts = new List<string>();

        foreach (var keyword in keywords)
        {
            var kw = keyword.ToLowerInvariant();
            if (name.Contains(kw, StringComparison.Ordinal))
            {
                score += Math.Min(25, 8 + kw.Length);
                parts.Add($"matches '{keyword}'");
            }
        }

        if (name.Length is >= 8 and <= 18)
        {
            score += 10;
            parts.Add("good length (8-18)");
        }
        else if (name.Length is >= 5 and <= 22)
        {
            score += 5;
            parts.Add("acceptable length");
        }
        else if (name.Length > 25)
        {
            score -= 5;
            parts.Add("long domain");
        }

        if (keywords.Count >= 2 && keywords.Take(2).All(k => name.Contains(k.ToLowerInvariant(), StringComparison.Ordinal)))
        {
            score += 15;
            parts.Add("multi-keyword match");
        }

        var geoModifiers = new[] { "mx", "global", "world", "app", "hub", "pro", "now", "auto" };
        if (geoModifiers.Any(m => name.EndsWith(m, StringComparison.Ordinal) || name.Contains(m, StringComparison.Ordinal)))
        {
            score += 5;
            parts.Add("modifier keyword");
        }

        score = Math.Clamp(score, 0, 100);
        var explanation = parts.Count > 0 ? string.Join("; ", parts) : "generic name";
        return new SeoScoreResult(score, explanation);
    }

    private static SeoScoreResult ScoreWithBrief(
        string domainName,
        IReadOnlyList<string> keywords,
        SearchBrief brief)
    {
        var name = domainName.ToLowerInvariant();
        var score = 40;
        var parts = new List<string> { "brand scoring" };

        if (name.Length is >= 6 and <= 9)
        {
            score += 15;
            parts.Add("ideal coined length (6-9)");
        }
        else if (name.Length is >= 5 and <= 12)
        {
            score += 5;
            parts.Add("acceptable length");
        }
        else
        {
            score -= 10;
            parts.Add("length penalty");
        }

        var literalTerms = keywords.Concat(brief.AvoidTerms).Distinct(StringComparer.OrdinalIgnoreCase);
        var keywordHits = 0;
        foreach (var term in literalTerms)
        {
            if (term.Length >= 3 && name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                keywordHits++;
                score -= 15;
                parts.Add($"literal '{term}' penalty");
            }
        }

        if (keywordHits == 1)
        {
            score += 10;
            parts.Add("single concept evoked");
        }
        else if (keywordHits >= 2)
        {
            score -= 20;
            parts.Add("keyword mashup penalty");
        }

        if (ModifierSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal)))
        {
            score -= 10;
            parts.Add("generic suffix penalty");
        }

        foreach (var concept in brief.ConceptKeywords)
        {
            if (concept.Length >= 3 && name.Contains(concept, StringComparison.OrdinalIgnoreCase) && keywordHits <= 1)
            {
                score += 5;
                parts.Add($"evokes '{concept}'");
                break;
            }
        }

        score = Math.Clamp(score, 0, 100);
        return new SeoScoreResult(score, string.Join("; ", parts));
    }
}
