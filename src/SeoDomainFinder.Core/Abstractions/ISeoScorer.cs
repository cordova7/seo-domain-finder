namespace SeoDomainFinder.Core.Abstractions;

public sealed record SeoScoreResult(int Score, string Explanation);

public interface ISeoScorer
{
    SeoScoreResult Score(string domainName, IReadOnlyList<string> keywords, string? language);
}
