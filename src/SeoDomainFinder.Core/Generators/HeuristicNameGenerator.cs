using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Models;
using SeoDomainFinder.Core.Services;

namespace SeoDomainFinder.Core.Generators;

public sealed class HeuristicNameGenerator : INameGenerator
{
    private static readonly string[] DefaultModifiers =
        ["app", "hub", "pro", "now", "auto", "online", "cloud", "digital", "smart", "get"];

    private static readonly Dictionary<string, string[]> ModifiersByLang = new()
    {
        ["es"] = ["app", "hub", "pro", "alerta", "alertas", "monitor", "auto", "online", "digital"],
        ["pt"] = ["app", "hub", "pro", "alerta", "alertas", "monitor", "auto", "online", "digital"],
        ["fr"] = ["app", "hub", "pro", "alerte", "alertes", "auto", "online", "digital"],
        ["de"] = ["app", "hub", "pro", "alert", "auto", "online", "digital", "monitor"],
        ["it"] = ["app", "hub", "pro", "auto", "online", "digitale"],
    };

    private static readonly Dictionary<string, string[]> ActionPrefixes = new()
    {
        ["en"] = ["get", "my", "go", "try", "use", "smart", "quick", "easy"],
        ["es"] = ["mi", "go", "alerta", "monitor", "rapido", "facil"],
        ["pt"] = ["meu", "go", "alerta", "monitor", "rapido", "facil"],
        ["fr"] = ["mon", "go", "alerte", "rapide", "facile"],
        ["de"] = ["mein", "go", "alert", "schnell", "einfach"],
    };

    public string Name => "heuristic";

    public Task<IReadOnlyList<string>> GenerateAsync(DomainSearchRequest request, CancellationToken ct = default)
    {
        var lang = KeywordExtractor.DetectLanguage(request.Prompt, request.Language);
        var keywords = KeywordExtractor.Extract(request.Prompt, lang);
        var modifiers = ModifiersByLang.GetValueOrDefault(lang, DefaultModifiers);
        var prefixes = ActionPrefixes.GetValueOrDefault(lang, ActionPrefixes["en"]);
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kw in keywords)
        {
            results.Add(NameSanitizer.Normalize(kw));
            foreach (var mod in modifiers.Take(8))
                results.Add(NameSanitizer.Normalize(kw + mod));
            foreach (var prefix in prefixes.Take(4))
                results.Add(NameSanitizer.Normalize(prefix + kw));
        }

        for (var i = 0; i < keywords.Count - 1; i++)
        {
            results.Add(NameSanitizer.Normalize(keywords[i] + keywords[i + 1]));
            if (i + 2 < keywords.Count)
                results.Add(NameSanitizer.Normalize(keywords[i] + keywords[i + 1] + keywords[i + 2]));
        }

        if (keywords.Count >= 2)
            results.Add(NameSanitizer.Normalize(string.Join("", keywords.Take(2))));

        foreach (var kw in keywords.Take(4))
        {
            results.Add(NameSanitizer.Normalize(kw + "ly"));
            results.Add(NameSanitizer.Normalize(kw + "ify"));
            results.Add(NameSanitizer.Normalize("my" + kw));
        }

        var filtered = results
            .Where(NameSanitizer.IsValidDomainName)
            .Take(request.MaxCandidates * 4)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(filtered);
    }
}
