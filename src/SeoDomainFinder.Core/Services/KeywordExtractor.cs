using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SeoDomainFinder.Core.Services;

public static partial class KeywordExtractor
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "for", "to", "of", "in", "on", "with", "my", "our", "your",
        "like", "but", "just", "very", "also", "than", "then", "when", "where", "who", "how",
        "el", "la", "los", "las", "un", "una", "de", "del", "para", "con", "por", "en", "y", "o",
        "que", "es", "son", "mi", "tu", "su", "le", "les", "des", "du", "et", "pour", "avec", "sur",
        "das", "der", "die", "und", "mit", "für", "da", "do", "dos", "em", "no", "na", "com", "e", "um", "uma",
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
        {
            var h = hint.Trim().ToLowerInvariant();
            return h.Length >= 2 ? h[..2] : h;
        }

        var lower = prompt.ToLowerInvariant();
        if (ContainsCjk(lower)) return "zh";
        if (ContainsArabic(lower)) return "ar";
        if (ContainsCyrillic(lower)) return "ru";
        if (Regex.IsMatch(lower, @"\b(el|la|los|de|para|con|negocio|proyecto|alertas)\b")) return "es";
        if (Regex.IsMatch(lower, @"\b(der|die|das|und|für|mit)\b")) return "de";
        if (Regex.IsMatch(lower, @"\b(le|la|les|pour|avec|entreprise)\b")) return "fr";
        if (Regex.IsMatch(lower, @"\b(não|para|empresa|negócio|com)\b")) return "pt";
        if (Regex.IsMatch(lower, @"\b(il|gli|per|con|azienda)\b")) return "it";
        if (Regex.IsMatch(lower, @"\b(と|の|を|です|ます)\b")) return "ja";
        return "en";
    }

    private static bool ContainsCjk(string text) =>
        text.Any(c => c is >= '\u4e00' and <= '\u9fff' or >= '\u3040' and <= '\u30ff');

    private static bool ContainsArabic(string text) =>
        text.Any(c => c is >= '\u0600' and <= '\u06ff');

    private static bool ContainsCyrillic(string text) =>
        text.Any(c => c is >= '\u0400' and <= '\u04ff');

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

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.IgnoreCase)]
    private static partial Regex WordRegex();
}
