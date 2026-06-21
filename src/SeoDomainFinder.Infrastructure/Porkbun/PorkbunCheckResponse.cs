using System.Text.Json.Serialization;

namespace SeoDomainFinder.Infrastructure.Porkbun;

public sealed class PorkbunCheckResponse
{
    public string Status { get; set; } = "";

    [JsonPropertyName("avail")]
    public string Avail { get; set; } = "";

    [JsonPropertyName("price")]
    public string Price { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    public string Message { get; set; } = "";

    [JsonPropertyName("ttlRemaining")]
    public int? TtlRemaining { get; set; }

    public PorkbunCheckInnerResponse? Response { get; set; }
}

public sealed class PorkbunCheckInnerResponse
{
    public string Avail { get; set; } = "";
    public string Price { get; set; } = "";
    public string Type { get; set; } = "";

    [JsonPropertyName("premium")]
    public string? Premium { get; set; }

    public bool IsPremium =>
        string.Equals(Premium, "yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Premium, "true", StringComparison.OrdinalIgnoreCase);
}
