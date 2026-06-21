using System.Text.RegularExpressions;

namespace SeoDomainFinder.Core.Services;

public static partial class NameSanitizer
{
    private static readonly HashSet<string> AllowedTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "net", "org", "io", "app", "ai", "mx", "dev", "co"
    };

    public static bool IsValidDomainName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name.Length < 3 || name.Length > 63)
            return false;
        if (name.Contains('-') || name.Any(char.IsDigit))
            return false;
        return DomainNameRegex().IsMatch(name);
    }

    public static string Normalize(string raw)
    {
        var cleaned = raw.Trim().ToLowerInvariant()
            .Replace(" ", "")
            .Replace("_", "");
        cleaned = NonAlphaNumRegex().Replace(cleaned, "");
        return cleaned;
    }

    public static bool IsAllowedTld(string tld) => AllowedTlds.Contains(tld.Trim().TrimStart('.'));

    [GeneratedRegex(@"^[a-z][a-z0-9]*$")]
    private static partial Regex DomainNameRegex();

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NonAlphaNumRegex();
}
