using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Abstractions;

public interface IDomainAdvisor
{
    Task<string?> AdviseAsync(SearchSummary summary, string? openRouterApiKey, CancellationToken ct = default);
}
