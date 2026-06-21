namespace SeoDomainFinder.Infrastructure.Options;

public sealed class PorkbunOptions
{
    public const string SectionName = "Porkbun";
    public string? ApiKey { get; set; }
    public string? SecretKey { get; set; }
    public int MinDelayMs { get; set; } = 2000;
}

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "openrouter/free";
}

public sealed class DemoRateLimitOptions
{
    public const string SectionName = "DemoRateLimit";
    public int LlmPerHour { get; set; } = 5;
    public int ChecksPerSession { get; set; } = 30;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public string AllowedOrigins { get; set; } = "http://localhost:3000";
}
