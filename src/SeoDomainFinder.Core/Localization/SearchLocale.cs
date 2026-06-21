namespace SeoDomainFinder.Core.Localization;

public static class SearchLocale
{
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "en", "es", "pt", "fr", "de"
    };

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "en";

        var code = language.Trim().ToLowerInvariant();
        if (code.Length >= 2)
            code = code[..2];

        return Supported.Contains(code) ? code : "en";
    }

    public static string Resolve(string? requestLanguage, string prompt)
    {
        if (!string.IsNullOrWhiteSpace(requestLanguage))
            return Normalize(requestLanguage);

        return Normalize(Services.KeywordExtractor.DetectLanguage(prompt, null));
    }

    public static string LlmLanguageName(string code) => Normalize(code) switch
    {
        "es" => "Spanish",
        "pt" => "Portuguese",
        "fr" => "French",
        "de" => "German",
        _ => "English"
    };

    public static string LlmResponseInstruction(string code)
    {
        var lang = LlmLanguageName(code);
        return $"""
            Write all natural-language output in {lang}.
            Domain labels: lowercase ASCII a-z only (no accents, no hyphens).
            Coin invented brand names using morphemes rooted in {lang}, not English defaults unless the UI language is English.
            """;
    }

    public static string LlmMorphemeHint(string code) => Normalize(code) switch
    {
        "es" => "Use Spanish morpheme roots (e.g. bici, cicl, estil, rod, velo) — not English bike/style.",
        "pt" => "Use Portuguese morpheme roots (e.g. cicl, estil, rod, velo) — not English defaults.",
        "fr" => "Use French morpheme roots (e.g. velo, styl, cycl, roue) — not English defaults.",
        "de" => "Use German morpheme roots (e.g. rad, fahr, styl, velo) — not English defaults.",
        _ => "Use English morpheme roots when coining labels."
    };
}
