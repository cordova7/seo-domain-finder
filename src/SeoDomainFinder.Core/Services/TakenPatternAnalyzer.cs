namespace SeoDomainFinder.Core.Services;

public sealed record TakenPatternAnalysis(
    string? PlannerHint,
    IReadOnlyList<string> SaturatedRoots,
    bool PreferNonCom);

public static class TakenPatternAnalyzer
{
    private static readonly string[] CommonRoots =
    [
        "connect", "book", "link", "engage", "fight", "batt", "clash", "punch",
        "net", "biz", "discov", "find"
    ];

    private static readonly string[] SpamSuffixes = ["ify", "ly", "ix", "hub"];

    public static TakenPatternAnalysis Analyze(
        IReadOnlyList<string> takenDomains,
        IReadOnlyList<string> allowedTlds)
    {
        if (takenDomains.Count == 0)
            return new TakenPatternAnalysis(null, [], false);

        var labels = takenDomains
            .Select(d => d.Split('.', 2)[0].ToLowerInvariant())
            .Where(l => l.Length > 0)
            .ToList();

        if (labels.Count == 0)
            return new TakenPatternAnalysis(null, [], false);

        var hints = new List<string>();
        var saturatedRoots = new List<string>();

        var suffixHits = labels.Count(l => SpamSuffixes.Any(s => l.EndsWith(s, StringComparison.Ordinal)));
        if (suffixHits >= (labels.Count + 1) / 2)
        {
            hints.Add("Avoid -ify, -ly, -ix, and -hub suffixes entirely.");
        }

        foreach (var root in CommonRoots)
        {
            var hits = labels.Count(l => l.Contains(root, StringComparison.Ordinal));
            if (hits >= Math.Max(2, (labels.Count + 2) / 3))
                saturatedRoots.Add(root);
        }

        if (saturatedRoots.Count > 0)
        {
            hints.Add(
                $"Avoid these saturated roots in labels: {string.Join(", ", saturatedRoots)}.");
        }

        var comOnly = takenDomains.All(d =>
            d.Contains('.', StringComparison.Ordinal) &&
            d.Split('.', 2)[1].Equals("com", StringComparison.OrdinalIgnoreCase));
        var hasAltTld = allowedTlds.Any(t => !t.Equals("com", StringComparison.OrdinalIgnoreCase));
        var preferNonCom = comOnly && hasAltTld && labels.Count >= 3;

        if (preferNonCom)
            hints.Add("All taken names were on .com — prefer other allowed TLDs for this batch.");

        hints.Add(
            "Generate NEW opaque portmanteaus (6-8 chars) that still evoke one conceptKeyword for SEO.");

        var hint = hints.Count > 0 ? string.Join(" ", hints) : null;
        return new TakenPatternAnalysis(hint, saturatedRoots, preferNonCom);
    }
}
