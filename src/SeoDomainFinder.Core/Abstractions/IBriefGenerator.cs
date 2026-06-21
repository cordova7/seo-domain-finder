using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Abstractions;

public sealed record BriefGeneratorRequest(
    string Prompt,
    string Language,
    IReadOnlyList<string> Tlds,
    string? OpenRouterApiKey);

public interface IBriefGenerator
{
    Task<SearchBrief> GenerateAsync(BriefGeneratorRequest request, CancellationToken ct = default);
}
