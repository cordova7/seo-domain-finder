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

    public static SearchBrief Create(string prompt, string lang, IReadOnlyList<string> keywords)
    {
        var avoidTerms = new HashSet<string>(TrademarkBlocklist, StringComparer.OrdinalIgnoreCase);
        foreach (var term in DetectMetaphorSources(prompt))
            avoidTerms.Add(term);

        var conceptKeywords = keywords
            .Where(k => !avoidTerms.Contains(k))
            .Take(6)
            .ToList();

        return new SearchBrief(
            ProductSummary: prompt.Length > 200 ? prompt[..200] : prompt,
            Audience: "target customers for this business",
            Vibe: ["professional", "memorable"],
            NamingStyles: DefaultNamingStyles,
            ConceptKeywords: conceptKeywords,
            AvoidTerms: avoidTerms.ToList(),
            AvoidPatterns: DefaultAvoidPatterns,
            TldStrategy: "prefer .com for main brand, .io for apps");
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
