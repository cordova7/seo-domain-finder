using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Services;

public static class SeoCoinedNameGenerator
{
    private static readonly string[] BlendPrefixes = ["nex", "ver", "tru", "pro", "zen", "max", "neo"];
    private static readonly string[] BlendSuffixes = ["io", "vo", "ra", "do", "ta", "ko", "on"];

    public static IReadOnlyList<string> Generate(
        SearchBrief brief,
        IReadOnlyList<string> keywords,
        int maxNames)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var concepts = brief.ConceptKeywords
            .Where(c => c.Length >= 3)
            .Take(6)
            .ToList();

        if (concepts.Count == 0)
        {
            foreach (var kw in keywords.Where(k => k.Length >= 3).Take(4))
                concepts.Add(kw);
        }

        foreach (var concept in concepts)
        {
            var stem = concept.Length <= 4 ? concept : concept[..4];
            foreach (var prefix in BlendPrefixes)
            {
                results.Add(NameSanitizer.Normalize(prefix + stem));
                if (stem.Length >= 3)
                    results.Add(NameSanitizer.Normalize(prefix + stem[..3]));
            }

            foreach (var suffix in BlendSuffixes)
            {
                if (stem.Length >= 3)
                    results.Add(NameSanitizer.Normalize(stem[..3] + suffix));
            }

            if (concept.Length >= 5)
            {
                results.Add(NameSanitizer.Normalize(concept[..3] + concept[^2..]));
                results.Add(NameSanitizer.Normalize(concept[..2] + "x" + concept[^2..]));
            }
        }

        for (var i = 0; i < concepts.Count - 1 && results.Count < maxNames * 2; i++)
        {
            var a = concepts[i];
            var b = concepts[i + 1];
            if (a.Length >= 3 && b.Length >= 3)
                results.Add(NameSanitizer.Normalize(a[..3] + b[..3]));
        }

        return results
            .Where(n => NameSanitizer.IsValidDomainName(n))
            .Where(n => n.Length is >= 6 and <= 9)
            .Where(n => DomainQualityFilter.IsAcceptable(n, brief, keywords, useLlm: true))
            .Take(maxNames)
            .ToList();
    }
}
