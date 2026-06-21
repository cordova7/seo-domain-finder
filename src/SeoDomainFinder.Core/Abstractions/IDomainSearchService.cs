using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Abstractions;

public interface IDomainSearchService
{
    Task<DomainSearchResult> SearchAsync(DomainSearchRequest request, CancellationToken ct = default);
}
