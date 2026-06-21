using System.Text.RegularExpressions;
using SeoDomainFinder.Core.Localization;
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
        "keyword stacks", "-hub", "-app", "-pro", "-ify", "-ly", "short trendy .com brands"
    ];

    public static SearchBrief Create(string prompt, string lang, IReadOnlyList<string> keywords)
    {
        var locale = SearchLocale.Normalize(lang);

        var avoidTerms = new HashSet<string>(TrademarkBlocklist, StringComparer.OrdinalIgnoreCase);
        foreach (var term in DetectMetaphorSources(prompt))
            avoidTerms.Add(term);
        foreach (var term in DetectTrademarkVariants(prompt))
            avoidTerms.Add(term);

        var conceptKeywords = keywords
            .Where(k => k.Length >= 3 && !avoidTerms.Contains(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (conceptKeywords.Count == 0)
        {
            conceptKeywords =
            [
                SearchStrings.Get(locale, "brief.defaultConceptVenture"),
                SearchStrings.Get(locale, "brief.defaultConceptBrand")
            ];
        }

        return new SearchBrief(
            ProductSummary: BuildProductSummary(prompt, locale),
            Audience: SearchStrings.Get(locale, "brief.audience"),
            Vibe:
            [
                SearchStrings.Get(locale, "brief.vibeMemorable"),
                SearchStrings.Get(locale, "brief.vibeDistinctive")
            ],
            NamingStyles:
            [
                SearchStrings.Get(locale, "brief.namingStyle1"),
                SearchStrings.Get(locale, "brief.namingStyle2"),
                SearchStrings.Get(locale, "brief.namingStyle3")
            ],
            ConceptKeywords: conceptKeywords,
            AvoidTerms: avoidTerms.ToList(),
            AvoidPatterns: DefaultAvoidPatterns,
            TldStrategy: SearchStrings.Get(locale, "brief.tldStrategy"));
    }

    internal static IEnumerable<string> DetectTrademarkVariants(string prompt)
    {
        if (TikTokPattern().IsMatch(prompt))
        {
            yield return "tiktok";
            yield return "tik";
            yield return "tok";
        }
    }

    [GeneratedRegex(@"\btik[\s\-]?tok\b", RegexOptions.IgnoreCase)]
    private static partial Regex TikTokPattern();

    private static string BuildProductSummary(string prompt, string locale)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return SearchStrings.Get(locale, "brief.productSummaryEmpty");

        var trimmed = prompt.Trim();
        if (trimmed.Length > 160)
            trimmed = trimmed[..157] + "...";

        return SearchStrings.Get(locale, "brief.productSummaryPrefix", trimmed);
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
