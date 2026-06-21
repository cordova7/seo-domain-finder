using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Services;

public static class DomainQualityFilter
{
    private static readonly string[] ModifierSuffixes =
        ["app", "hub", "pro", "now", "auto", "online", "digital", "cloud"];

    public static bool IsAcceptable(
        string label,
        SearchBrief? brief,
        IReadOnlyList<string> keywords,
        bool useLlm)
    {
        if (!NameSanitizer.IsValidDomainName(label))
            return false;

        if (!useLlm || brief is null)
            return true;

        var name = label.ToLowerInvariant();

        if (name.Length is < 5 or > 14)
            return false;

        foreach (var term in brief.AvoidTerms)
        {
            if (term.Length >= 3 && name.Contains(term, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var keywordHits = CountKeywordHits(name, keywords);
        var conceptHits = CountKeywordHits(name, brief.ConceptKeywords);

        if (keywordHits >= 2 || conceptHits >= 2)
            return false;

        if (name.Length > 10 && ModifierSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal)))
            return false;

        return true;
    }

    private static int CountKeywordHits(string name, IReadOnlyList<string> terms)
    {
        var hits = 0;
        foreach (var term in terms)
        {
            if (term.Length >= 3 && name.Contains(term, StringComparison.OrdinalIgnoreCase))
                hits++;
        }

        return hits;
    }
}
