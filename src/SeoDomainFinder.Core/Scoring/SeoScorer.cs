using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Localization;
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
        var lang = SearchLocale.Normalize(language);

        if (brief is null)
            return ScoreClassic(domainName, keywords, lang);

        return ScoreWithBrief(domainName, keywords, brief, lang);
    }

    private static SeoScoreResult ScoreClassic(string domainName, IReadOnlyList<string> keywords, string lang)
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
                parts.Add(SearchStrings.Get(lang, "seo.matches", keyword));
            }
        }

        if (name.Length is >= 8 and <= 18)
        {
            score += 10;
            parts.Add(SearchStrings.Get(lang, "seo.goodLength"));
        }
        else if (name.Length is >= 5 and <= 22)
        {
            score += 5;
            parts.Add(SearchStrings.Get(lang, "seo.acceptableLength"));
        }
        else if (name.Length > 25)
        {
            score -= 5;
            parts.Add(SearchStrings.Get(lang, "seo.longDomain"));
        }

        if (keywords.Count >= 2 && keywords.Take(2).All(k => name.Contains(k.ToLowerInvariant(), StringComparison.Ordinal)))
        {
            score += 15;
            parts.Add(SearchStrings.Get(lang, "seo.multiKeywordMatch"));
        }

        var geoModifiers = new[] { "mx", "global", "world", "app", "hub", "pro", "now", "auto" };
        if (geoModifiers.Any(m => name.EndsWith(m, StringComparison.Ordinal) || name.Contains(m, StringComparison.Ordinal)))
        {
            score += 5;
            parts.Add(SearchStrings.Get(lang, "seo.modifierKeyword"));
        }

        score = Math.Clamp(score, 0, 100);
        var explanation = parts.Count > 0
            ? string.Join("; ", parts)
            : SearchStrings.Get(lang, "seo.genericName");
        return new SeoScoreResult(score, explanation);
    }

    private static SeoScoreResult ScoreWithBrief(
        string domainName,
        IReadOnlyList<string> keywords,
        SearchBrief brief,
        string lang)
    {
        var name = domainName.ToLowerInvariant();
        var score = 40;
        var parts = new List<string> { SearchStrings.Get(lang, "seo.brandScoring") };

        if (name.Length is >= 8 and <= 11)
        {
            score += 15;
            parts.Add(SearchStrings.Get(lang, "seo.idealBrandLength"));
        }
        else if (name.Length is >= 6 and <= 12)
        {
            score += 8;
            parts.Add(SearchStrings.Get(lang, "seo.acceptableCoinedLength"));
        }
        else
        {
            score -= 5;
            parts.Add(SearchStrings.Get(lang, "seo.lengthPenalty"));
        }

        if (HasSoftMetaphorShape(name))
        {
            score += 8;
            parts.Add(SearchStrings.Get(lang, "seo.softMetaphorPortmanteau"));
        }

        var literalTerms = keywords.Concat(brief.AvoidTerms).Distinct(StringComparer.OrdinalIgnoreCase);
        var keywordHits = 0;
        foreach (var term in literalTerms)
        {
            if (term.Length >= 3 && name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                keywordHits++;
                score -= 15;
                parts.Add(SearchStrings.Get(lang, "seo.literalPenalty", term));
            }
        }

        if (keywordHits == 1)
        {
            score += 10;
            parts.Add(SearchStrings.Get(lang, "seo.singleConceptEvoked"));
        }
        else if (keywordHits >= 2)
        {
            score -= 20;
            parts.Add(SearchStrings.Get(lang, "seo.keywordMashupPenalty"));
        }

        if (ModifierSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal)))
        {
            score -= 10;
            parts.Add(SearchStrings.Get(lang, "seo.genericSuffixPenalty"));
        }

        foreach (var concept in brief.ConceptKeywords)
        {
            if (concept.Length >= 3 && name.Contains(concept, StringComparison.OrdinalIgnoreCase) && keywordHits <= 1)
            {
                score += 5;
                parts.Add(SearchStrings.Get(lang, "seo.evokes", concept));
                break;
            }
        }

        score = Math.Clamp(score, 0, 100);
        return new SeoScoreResult(score, string.Join("; ", parts));
    }

    private static readonly string[] MetaphorTails =
    [
        "crate", "stall", "mart", "shelf", "drop", "bay", "nook", "loft", "haus", "port", "cart",
        "buzz", "vibe", "rush", "spark", "wave", "flux", "glow", "pulse"
    ];

    private static bool HasSoftMetaphorShape(string name) =>
        MetaphorTails.Any(t => name.EndsWith(t, StringComparison.OrdinalIgnoreCase));
}
