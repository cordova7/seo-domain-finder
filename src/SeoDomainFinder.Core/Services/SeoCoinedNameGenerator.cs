using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Services;

public static class SeoCoinedNameGenerator
{
    private static readonly string[] CommerceTails =
        ["crate", "stall", "mart", "shelf", "drop", "bay", "nook", "loft", "haus", "port", "cart"];

    private static readonly string[] EnergyTails =
        ["buzz", "vibe", "rush", "spark", "wave", "flux", "glow", "pulse", "pop"];

    private static readonly string[] ObscureInfixes = ["zo", "qv", "vx", "zk"];

    public static IReadOnlyList<string> Generate(
        SearchBrief brief,
        IReadOnlyList<string> keywords,
        int maxNames,
        IReadOnlySet<string>? excludeLabels = null)
    {
        var concepts = brief.ConceptKeywords
            .Where(c => c.Length >= 3)
            .Take(6)
            .ToList();

        if (concepts.Count == 0)
        {
            concepts = keywords
                .Where(k => k.Length >= 3)
                .Take(4)
                .ToList();
        }

        var results = new List<string>();
        results.AddRange(GenerateMetaphorBlends(concepts));
        results.AddRange(GenerateObscureBlends(concepts));

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => NameSanitizer.IsValidDomainName(n))
            .Where(n => n.Length is >= 7 and <= 12)
            .Where(n => excludeLabels is null || !excludeLabels.Contains(n))
            .Where(n => DomainQualityFilter.IsAcceptable(n, brief, keywords, useLlm: true))
            .OrderByDescending(n => BrandabilityRank(n))
            .Take(maxNames)
            .ToList();
    }

    private static IEnumerable<string> GenerateMetaphorBlends(IReadOnlyList<string> concepts)
    {
        foreach (var concept in concepts)
        {
            var stem4 = concept.Length >= 4 ? concept[..4] : concept.PadRight(3, 'x')[..3];
            var stem3 = stem4[..3];

            foreach (var tail in CommerceTails.Concat(EnergyTails))
            {
                yield return NameSanitizer.Normalize(stem3 + tail);
                if (stem4.Length == 4 && stem4 != stem3)
                    yield return NameSanitizer.Normalize(stem4 + tail[..3]);
            }
        }

        for (var i = 0; i < concepts.Count - 1; i++)
        {
            var a = concepts[i];
            var b = concepts[i + 1];
            if (a.Length >= 3 && b.Length >= 3)
            {
                yield return NameSanitizer.Normalize(a[..3] + b[..3] + "co");
                yield return NameSanitizer.Normalize(a[..2] + b[..3] + "go");
            }
        }
    }

    private static IEnumerable<string> GenerateObscureBlends(IReadOnlyList<string> concepts)
    {
        foreach (var concept in concepts)
        {
            var stem = concept.Length >= 4 ? concept[..4] : concept.PadRight(3, 'x')[..3];
            foreach (var infix in ObscureInfixes)
            {
                yield return NameSanitizer.Normalize(stem + infix + stem[^1..]);
                yield return NameSanitizer.Normalize(stem[..3] + infix + stem[^2..]);
            }
        }
    }

    private static int BrandabilityRank(string name)
    {
        var score = 0;
        if (name.Length is >= 8 and <= 10)
            score += 3;
        else if (name.Length is >= 7 and <= 12)
            score += 1;

        if (CommerceTails.Any(t => name.EndsWith(t, StringComparison.OrdinalIgnoreCase)))
            score += 4;

        if (EnergyTails.Any(t => name.EndsWith(t, StringComparison.OrdinalIgnoreCase)))
            score += 3;

        if (name.Count(c => "aeiou".Contains(c)) >= 2)
            score += 2;

        return score;
    }
}
