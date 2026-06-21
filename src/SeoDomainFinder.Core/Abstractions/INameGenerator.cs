using SeoDomainFinder.Core.Models;

namespace SeoDomainFinder.Core.Abstractions;

public interface INameGenerator
{
    string Name { get; }
    Task<IReadOnlyList<string>> GenerateAsync(DomainSearchRequest request, CancellationToken ct = default);
}
