using SeoDomainFinder.Core.Localization;
using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Services;

public static class ShortAdviceBuilder
{
    public static string Build(
        IReadOnlyList<DomainCandidate> ranked,
        int checksUsed,
        int maxChecks,
        int unavailableCount,
        string? language)
    {
        var lang = SearchLocale.Normalize(language);

        if (ranked.Count == 0)
        {
            return SearchStrings.Get(lang, "advice.noneFound", checksUsed, maxChecks, unavailableCount);
        }

        if (ranked.Count == 1)
        {
            var top = ranked[0];
            var price = top.PriceUsd is { } p ? $" (${p:F2})" : "";
            return SearchStrings.Get(lang, "advice.oneFound", top.FullDomain, price, checksUsed, maxChecks);
        }

        return SearchStrings.Get(lang, "advice.multipleFound", ranked.Count, checksUsed, maxChecks, unavailableCount);
    }
}
