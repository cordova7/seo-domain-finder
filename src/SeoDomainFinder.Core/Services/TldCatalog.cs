namespace SeoDomainFinder.Core.Services;

public static class TldCatalog
{
    public static readonly string[] Universal = ["com", "io", "net", "app", "org", "dev", "ai"];

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CountryTld> Countries { get; }

    static TldCatalog()
    {
        Countries =
        [
            // Americas
            new("AR", "Argentina", "ar", "americas", ["es"]),
            new("BO", "Bolivia", "bo", "americas", ["es"]),
            new("BR", "Brazil", "br", "americas", ["pt"]),
            new("CA", "Canada", "ca", "americas", ["en", "fr"]),
            new("CL", "Chile", "cl", "americas", ["es"]),
            new("CO", "Colombia", "co", "americas", ["es"]),
            new("CR", "Costa Rica", "cr", "americas", ["es"]),
            new("CU", "Cuba", "cu", "americas", ["es"]),
            new("DO", "Dominican Republic", "do", "americas", ["es"]),
            new("EC", "Ecuador", "ec", "americas", ["es"]),
            new("GT", "Guatemala", "gt", "americas", ["es"]),
            new("HN", "Honduras", "hn", "americas", ["es"]),
            new("MX", "Mexico", "mx", "americas", ["es"]),
            new("NI", "Nicaragua", "ni", "americas", ["es"]),
            new("PA", "Panama", "pa", "americas", ["es"]),
            new("PE", "Peru", "pe", "americas", ["es"]),
            new("PY", "Paraguay", "py", "americas", ["es"]),
            new("SV", "El Salvador", "sv", "americas", ["es"]),
            new("US", "United States", "us", "americas", ["en"]),
            new("UY", "Uruguay", "uy", "americas", ["es"]),
            new("VE", "Venezuela", "ve", "americas", ["es"]),
            // Europe
            new("AT", "Austria", "at", "europe", ["de"]),
            new("BE", "Belgium", "be", "europe", ["nl", "fr", "de"]),
            new("BG", "Bulgaria", "bg", "europe", ["bg"]),
            new("CH", "Switzerland", "ch", "europe", ["de", "fr", "it"]),
            new("CY", "Cyprus", "cy", "europe", ["el", "tr"]),
            new("CZ", "Czech Republic", "cz", "europe", ["cs"]),
            new("DE", "Germany", "de", "europe", ["de"]),
            new("DK", "Denmark", "dk", "europe", ["da"]),
            new("EE", "Estonia", "ee", "europe", ["et"]),
            new("ES", "Spain", "es", "europe", ["es"]),
            new("FI", "Finland", "fi", "europe", ["fi", "sv"]),
            new("FR", "France", "fr", "europe", ["fr"]),
            new("GB", "United Kingdom", "uk", "europe", ["en"]),
            new("GR", "Greece", "gr", "europe", ["el"]),
            new("HR", "Croatia", "hr", "europe", ["hr"]),
            new("HU", "Hungary", "hu", "europe", ["hu"]),
            new("IE", "Ireland", "ie", "europe", ["en"]),
            new("IS", "Iceland", "is", "europe", ["is"]),
            new("IT", "Italy", "it", "europe", ["it"]),
            new("LT", "Lithuania", "lt", "europe", ["lt"]),
            new("LU", "Luxembourg", "lu", "europe", ["fr", "de"]),
            new("LV", "Latvia", "lv", "europe", ["lv"]),
            new("MT", "Malta", "mt", "europe", ["en", "mt"]),
            new("NL", "Netherlands", "nl", "europe", ["nl"]),
            new("NO", "Norway", "no", "europe", ["no"]),
            new("PL", "Poland", "pl", "europe", ["pl"]),
            new("PT", "Portugal", "pt", "europe", ["pt"]),
            new("RO", "Romania", "ro", "europe", ["ro"]),
            new("RS", "Serbia", "rs", "europe", ["sr"]),
            new("RU", "Russia", "ru", "europe", ["ru"]),
            new("SE", "Sweden", "se", "europe", ["sv"]),
            new("SI", "Slovenia", "si", "europe", ["sl"]),
            new("SK", "Slovakia", "sk", "europe", ["sk"]),
            new("UA", "Ukraine", "ua", "europe", ["uk"]),
            // Asia
            new("AE", "UAE", "ae", "asia", ["ar"]),
            new("CN", "China", "cn", "asia", ["zh"]),
            new("HK", "Hong Kong", "hk", "asia", ["zh", "en"]),
            new("ID", "Indonesia", "id", "asia", ["id"]),
            new("IL", "Israel", "il", "asia", ["he"]),
            new("IN", "India", "in", "asia", ["hi", "en"]),
            new("JP", "Japan", "jp", "asia", ["ja"]),
            new("KR", "South Korea", "kr", "asia", ["ko"]),
            new("MY", "Malaysia", "my", "asia", ["ms"]),
            new("PH", "Philippines", "ph", "asia", ["en", "tl"]),
            new("PK", "Pakistan", "pk", "asia", ["ur"]),
            new("SA", "Saudi Arabia", "sa", "asia", ["ar"]),
            new("SG", "Singapore", "sg", "asia", ["en", "zh", "ms"]),
            new("TH", "Thailand", "th", "asia", ["th"]),
            new("TR", "Turkey", "tr", "asia", ["tr"]),
            new("TW", "Taiwan", "tw", "asia", ["zh"]),
            new("VN", "Vietnam", "vn", "asia", ["vi"]),
            // Africa
            new("DZ", "Algeria", "dz", "africa", ["ar", "fr"]),
            new("EG", "Egypt", "eg", "africa", ["ar"]),
            new("KE", "Kenya", "ke", "africa", ["en", "sw"]),
            new("MA", "Morocco", "ma", "africa", ["ar", "fr"]),
            new("NG", "Nigeria", "ng", "africa", ["en"]),
            new("TN", "Tunisia", "tn", "africa", ["ar"]),
            new("ZA", "South Africa", "za", "africa", ["en", "af", "zu"]),
            // Oceania
            new("AU", "Australia", "au", "oceania", ["en"]),
            new("NZ", "New Zealand", "nz", "oceania", ["en", "mi"]),
        ];

        foreach (var tld in Universal)
            Allowed.Add(tld);
        foreach (var c in Countries)
            Allowed.Add(c.Tld);
    }

    public static bool IsAllowed(string tld) => Allowed.Contains(tld.Trim().TrimStart('.'));

    public static IReadOnlyList<string> ForLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return [];
        var code = lang.Trim().ToLowerInvariant();
        if (code.Length > 2)
            code = code[..2];
        return Countries.Where(c => c.Languages.Contains(code)).Select(c => c.Tld).ToList();
    }
}

public sealed record CountryTld(string Code, string Name, string Tld, string Region, string[] Languages);
