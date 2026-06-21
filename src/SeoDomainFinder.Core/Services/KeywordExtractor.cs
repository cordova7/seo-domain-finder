using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SeoDomainFinder.Core.Services;

public static partial class KeywordExtractor
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "for", "to", "of", "in", "on", "with", "my", "our", "your",
        "el", "la", "los", "las", "un", "una", "de", "del", "para", "con", "por", "en", "y", "o",
        "que", "es", "son", "mi", "tu", "su", "le", "les", "des", "du", "de", "la", "les", "et",
        "pour", "avec", "sur", "das", "der", "die", "und", "mit", "für", "um", "o", "a", "de",
        "da", "do", "dos", "das", "em", "no", "na", "para", "com", "por", "e", "um", "uma",
        "business", "project", "company", "startup", "app", "application", "website", "service",
        "negocio", "proyecto", "empresa", "aplicacion", "aplicación", "sitio", "servicio",
        "want", "need", "looking", "create", "build", "make", "quiero", "necesito", "busco"
    };

    public static IReadOnlyList<string> Extract(string prompt, string? languageHint)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return [];

        var normalized = RemoveDiacritics(prompt.ToLowerInvariant());
        var tokens = WordRegex().Matches(normalized)
            .Select(m => m.Value)
            .Where(t => t.Length >= 3 && !StopWords.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        if (tokens.Count == 0)
        {
            tokens = WordRegex().Matches(normalized)
                .Select(m => m.Value)
                .Where(t => t.Length >= 2)
                .Take(6)
                .ToList();
        }

        return tokens;
    }

    public static string DetectLanguage(string prompt, string? hint)
    {
        if (!string.IsNullOrWhiteSpace(hint))
            return hint.Trim().ToLowerInvariant()[..Math.Min(2, hint.Trim().Length)];

        var lower = prompt.ToLowerInvariant();
        if (Regex.IsMatch(lower, @"\b(el|la|los|de|para|con|negocio|proyecto|alertas|judicial)\b"))
            return "es";
        if (Regex.IsMatch(lower, @"\b(der|die|das|und|für|mit|geschäft)\b"))
            return "de";
        if (Regex.IsMatch(lower, @"\b(le|la|les|pour|avec|entreprise)\b"))
            return "fr";
        if (Regex.IsMatch(lower, @"\b(o|a|de|para|com|empresa|negócio)\b"))
            return "pt";
        return "en";
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex WordRegex();
}
