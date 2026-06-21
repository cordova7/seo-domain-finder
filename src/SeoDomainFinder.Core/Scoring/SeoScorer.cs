using SeoDomainFinder.Core.Abstractions;

namespace SeoDomainFinder.Core.Scoring;

public sealed class SeoScorer : ISeoScorer
{
    public SeoScoreResult Score(string domainName, IReadOnlyList<string> keywords, string? language)
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
}
