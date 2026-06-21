namespace SeoDomainFinder.Core.Models;

public static class DomainCheckReasons
{
    public const string RateLimited = "rate_limited";
    public const string CredentialsMissing = "Porkbun API credentials not configured";
    public const string Unavailable = "unavailable";
    public const string Premium = "premium";
}
