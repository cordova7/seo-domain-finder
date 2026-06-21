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
        "keyword stacks", "-hub", "-app", "-pro", "-ify", "-ly", "short trendy .com brands"
    ];

    private static readonly string[] DefaultNamingStyles =
    [
        "soft metaphor portmanteaus (fadcrate, buzzstall)",
        "8-11 char opaque blends",
        "abstract coined brands"
    ];

  private static readonly string[] DefaultVibe = ["memorable", "distinctive"];

    public static SearchBrief Create(string prompt, string lang, IReadOnlyList<string> keywords)
    {
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
            conceptKeywords = ["venture", "brand"];

        return new SearchBrief(
            ProductSummary: BuildProductSummary(prompt),
            Audience: "target customers for this business",
            Vibe: DefaultVibe,
            NamingStyles: DefaultNamingStyles,
            ConceptKeywords: conceptKeywords,
            AvoidTerms: avoidTerms.ToList(),
            AvoidPatterns: DefaultAvoidPatterns,
            TldStrategy: "short pronounceable .com names are usually taken; prefer 8-10 char opaque blends on .com");
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
