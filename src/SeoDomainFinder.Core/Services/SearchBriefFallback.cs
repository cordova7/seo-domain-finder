using System.Text.RegularExpressions;
using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Services;

public static partial class SearchBriefFallback
{
    private static readonly string[] TrademarkBlocklist =
    [
        "tinder", "bumble", "hinge", "google", "facebook", "meta", "amazon",
        "uber", "lyft", "netflix", "spotify", "apple", "microsoft", "twitter",
        "instagram", "tiktok", "youtube", "paypal", "stripe", "shopify"
    ];

    private static readonly string[] DefaultAvoidPatterns =
    [
        "keyword stacks", "-hub", "-app", "-pro", "-ify", "-ly"
    ];

    private static readonly string[] DefaultNamingStyles =
    [
        "coined 6-9 char brands", "portmanteaus", "abstract verbs"
    ];

    private static readonly string[] AbstractConceptKeywords =
    [
        "matchup", "community", "brand", "venture"
    ];

    public static SearchBrief Create(string prompt, string lang, IReadOnlyList<string> keywords)
    {
        var avoidTerms = new HashSet<string>(TrademarkBlocklist, StringComparer.OrdinalIgnoreCase);
        foreach (var term in DetectMetaphorSources(prompt))
            avoidTerms.Add(term);

        return new SearchBrief(
            ProductSummary: BuildProductSummary(prompt),
            Audience: "target customers for this business",
            Vibe: ["professional", "memorable"],
            NamingStyles: DefaultNamingStyles,
            ConceptKeywords: AbstractConceptKeywords.ToList(),
            AvoidTerms: avoidTerms.ToList(),
            AvoidPatterns: DefaultAvoidPatterns,
            TldStrategy: "prefer .com for main brand, .io for apps");
    }

    private static string BuildProductSummary(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return "A new business venture seeking a memorable domain name.";

        var trimmed = prompt.Trim();
        if (trimmed.Length > 160)
            trimmed = trimmed[..157] + "...";

        return $"A business concept described as: {trimmed}";
    }

    internal static IEnumerable<string> DetectMetaphorSources(string prompt)
    {
        var match = MetaphorPattern().Match(prompt);
        if (!match.Success)
            yield break;

        var source = match.Groups[1].Value.Trim().ToLowerInvariant();
        if (source.Length >= 3)
            yield return source;

        foreach (var token in KeywordExtractor.Extract(prompt, "en"))
        {
            if (TrademarkBlocklist.Contains(token, StringComparer.OrdinalIgnoreCase))
                yield return token;
        }
    }

    [GeneratedRegex(@"\b(\w+)\s+but\s+for\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetaphorPattern();
}
