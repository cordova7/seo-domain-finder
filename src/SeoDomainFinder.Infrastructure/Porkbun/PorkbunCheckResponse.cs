namespace SeoDomainFinder.Infrastructure.Porkbun;

public sealed class PorkbunCheckResponse
{
    public string Status { get; set; } = "";
    public string Avail { get; set; } = "";
    public string Price { get; set; } = "";
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
}
