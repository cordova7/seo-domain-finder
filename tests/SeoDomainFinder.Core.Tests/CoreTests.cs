using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Generators;
using SeoDomainFinder.Core.Models;
using SeoDomainFinder.Core.Scoring;
using SeoDomainFinder.Core.Services;

namespace SeoDomainFinder.Core.Tests;

public class KeywordExtractorTests
{
    [Fact]
    public void Extract_FiltersStopWords()
    {
        var keywords = KeywordExtractor.Extract(
            "I want to build a judicial alert monitoring app for lawyers in Mexico",
            "en");

        Assert.Contains(keywords, k => k.Contains("judicial", StringComparison.OrdinalIgnoreCase) ||
                                       k.Contains("alert", StringComparison.OrdinalIgnoreCase) ||
                                       k.Contains("lawyer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keywords, k => k.Equals("want", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DetectLanguage_Spanish()
    {
        var lang = KeywordExtractor.DetectLanguage("alertas judiciales para abogados en México", null);
        Assert.Equal("es", lang);
    }
}

public class NameSanitizerTests
{
    [Theory]
    [InlineData("alertasjud", true)]
    [InlineData("my-domain", false)]
    [InlineData("domain123", false)]
    [InlineData("ab", false)]
    public void IsValidDomainName_Works(string name, bool expected)
    {
        Assert.Equal(expected, NameSanitizer.IsValidDomainName(name));
    }

    [Fact]
    public void Normalize_RemovesInvalidChars()
    {
        Assert.Equal("alertasjud", NameSanitizer.Normalize("Alertas-Jud!!!"));
    }

    [Theory]
    [InlineData("com", true)]
    [InlineData("mx", true)]
    [InlineData("invalidtld", false)]
    public void IsAllowedTld_Works(string tld, bool expected)
    {
        Assert.Equal(expected, NameSanitizer.IsAllowedTld(tld));
    }
}

public class TldCatalogTests
{
    [Fact]
    public void Universal_IncludesCommonTlds()
    {
        Assert.Contains("com", TldCatalog.Universal);
        Assert.Contains("io", TldCatalog.Universal);
        Assert.DoesNotContain("mx", TldCatalog.Universal);
    }

    [Fact]
    public void ForLanguage_ReturnsCountryTlds()
    {
        var mx = TldCatalog.ForLanguage("es");
        Assert.Contains("mx", mx);
        Assert.Contains("es", mx);
    }
}

public class SeoScorerTests
{
    [Fact]
    public void Score_MatchesKeywords()
    {
        var scorer = new SeoScorer();
        var result = scorer.Score("alertasjud", ["alertas", "judicial", "mexico"], "es");

        Assert.True(result.Score > 0);
        Assert.Contains("alertas", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }
}

public class HeuristicNameGeneratorTests
{
    [Fact]
    public async Task Generate_ReturnsCandidates()
    {
        var gen = new HeuristicNameGenerator();
        var names = await gen.GenerateAsync(new DomainSearchRequest
        {
            Prompt = "judicial alert monitoring for lawyers",
            Language = "en",
            MaxCandidates = 10
        });

        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.True(NameSanitizer.IsValidDomainName(n)));
    }

    [Fact]
    public async Task Generate_DoesNotAppendMxSuffix()
    {
        var gen = new HeuristicNameGenerator();
        var names = await gen.GenerateAsync(new DomainSearchRequest
        {
            Prompt = "pet shop for dogs",
            Language = "en",
            MaxCandidates = 20
        });

        Assert.DoesNotContain(names, n => n.EndsWith("mx", StringComparison.OrdinalIgnoreCase));
    }
}

public class DomainSearchServiceTests
{
    [Fact]
    public async Task Search_WithPlanner_SkipsLlmNameGenAndUsesPlannerQueue()
    {
        var checker = new FakeChecker(availableAfter: 1);
        var planner = new FakePlanner(
        [
            new PlannedCheck("pawlynx", "com", 90),
            new PlannedCheck("walklio", "io", 85)
        ]);
        var service = new DomainSearchService(
            [new HeuristicNameGenerator()],
            new SeoScorer(),
            checker,
            planner);

        var result = await service.SearchAsync(new DomainSearchRequest
        {
            Prompt = "dog walking business",
            Language = "en",
            Tlds = ["com", "io"],
            UseLlm = true,
            MaxCandidates = 5,
            MaxChecks = 10
        });

        Assert.Equal("heuristic+planner", result.GeneratorUsed);
        Assert.Contains(result.Candidates, c => c.FullDomain is "pawlynx.com" or "walklio.io");
    }

    [Fact]
    public async Task Search_WithPlanner_TriggersRefillWhenQueueExhausted()
    {
        var checker = new FakeChecker(availableAfter: int.MaxValue);
        var planner = new RefillingFakePlanner();
        var service = new DomainSearchService(
            [new HeuristicNameGenerator()],
            new SeoScorer(),
            checker,
            planner);
        var events = new List<SearchProgressEvent>();

        await service.SearchAsync(
            new DomainSearchRequest
            {
                Prompt = "tinder but for hood street fighters",
                Language = "en",
                Tlds = ["com", "io"],
                UseLlm = true,
                MaxCandidates = 5,
                MaxChecks = 25
            },
            new Progress<SearchProgressEvent>(events.Add));

        Assert.Equal(2, planner.CallCount);
        Assert.Contains(events, e => e.Phase == "refining");
        var maxChecksUsed = events.Max(e => e.ChecksUsed);
        Assert.True(maxChecksUsed > 15, $"Expected >15 checks, got {maxChecksUsed}");
    }

    [Fact]
    public void DeriveTakenPatternHint_DetectsSuffixHeavyNames()
    {
        var hint = DomainSearchService.DeriveTakenPatternHint(
        [
            "tindify.com",
            "hoodify.io",
            "streetly.com",
            "fightr.com"
        ]);

        Assert.NotNull(hint);
        Assert.Contains("-ify", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_WarningWithAiOn_DoesNotSuggestEnableAi()
    {
        var checker = new FakeChecker(availableAfter: int.MaxValue);
        var planner = new FakePlanner([new PlannedCheck("takenname", "com", 90)]);
        var service = new DomainSearchService(
            [new HeuristicNameGenerator()],
            new SeoScorer(),
            checker,
            planner);

        var result = await service.SearchAsync(new DomainSearchRequest
        {
            Prompt = "niche startup",
            Language = "en",
            Tlds = ["com"],
            UseLlm = true,
            MaxCandidates = 3,
            MaxChecks = 10
        });

        Assert.NotNull(result.Warning);
        Assert.DoesNotContain("enable AI", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invented", result.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_ReturnsOnlyAvailableDomains()
    {
        var checker = new FakeChecker(availableAfter: 3);
        var service = new DomainSearchService(
            [new HeuristicNameGenerator()],
            new SeoScorer(),
            checker);

        var result = await service.SearchAsync(new DomainSearchRequest
        {
            Prompt = "pet shop for dogs",
            Language = "en",
            Tlds = ["com"],
            MaxCandidates = 5,
            MaxChecks = 20
        });

        Assert.NotEmpty(result.Candidates);
        Assert.All(result.Candidates, c => Assert.True(c.Available));
        Assert.All(result.Candidates, c => Assert.NotNull(c.PriceUsd));
    }

    [Fact]
    public async Task Search_EmptyWhenNoneAvailable()
    {
        var checker = new FakeChecker(availableAfter: int.MaxValue);
        var service = new DomainSearchService(
            [new HeuristicNameGenerator()],
            new SeoScorer(),
            checker);

        var result = await service.SearchAsync(new DomainSearchRequest
        {
            Prompt = "pet shop for dogs",
            Language = "en",
            Tlds = ["com"],
            MaxCandidates = 5,
            MaxChecks = 10
        });

        Assert.Empty(result.Candidates);
        Assert.Contains("none available", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_WarnsWhenCredentialsMissing()
    {
        var checker = new FakeChecker(credentialsMissing: true);
        var service = new DomainSearchService(
            [new HeuristicNameGenerator()],
            new SeoScorer(),
            checker);

        var result = await service.SearchAsync(new DomainSearchRequest
        {
            Prompt = "pet shop for dogs",
            Language = "en",
            Tlds = ["com"],
            MaxCandidates = 5,
            MaxChecks = 5
        });

        Assert.Empty(result.Candidates);
        Assert.Contains("not configured", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_DoesNotCountRateLimitedChecks()
    {
        var checker = new FakeChecker(rateLimited: true);
        var service = new DomainSearchService(
            [new HeuristicNameGenerator()],
            new SeoScorer(),
            checker);

        var result = await service.SearchAsync(new DomainSearchRequest
        {
            Prompt = "pet shop for dogs",
            Language = "en",
            Tlds = ["com"],
            MaxCandidates = 5,
            MaxChecks = 5
        });

        Assert.Empty(result.Candidates);
        Assert.Contains("rate limit", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_ReportsProgressEvents()
    {
        var checker = new FakeChecker(availableAfter: 1);
        var service = new DomainSearchService(
            [new HeuristicNameGenerator()],
            new SeoScorer(),
            checker);
        var events = new List<SearchProgressEvent>();

        await service.SearchAsync(
            new DomainSearchRequest
            {
                Prompt = "pet shop for dogs",
                Language = "en",
                Tlds = ["com"],
                MaxCandidates = 3,
                MaxChecks = 10
            },
            new Progress<SearchProgressEvent>(events.Add));

        Assert.Contains(events, e => e.Phase == "generating");
        Assert.Contains(events, e => e.Phase == "checking");
        Assert.Contains(events, e => e.Phase == "done");
    }

    [Fact]
    public void OrderTldsComFirst_PutsComFirst()
    {
        var ordered = DomainSearchService.OrderTldsComFirst(["io", "net", "com"]);
        Assert.Equal("com", ordered[0]);
        Assert.Equal("io", ordered[1]);
    }

    [Fact]
    public void BuildCandidateQueue_PrefersComBeforeOtherTlds()
    {
        var service = new DomainSearchService(
            [new HeuristicNameGenerator()],
            new SeoScorer(),
            new FakeChecker());

        var queue = service.BuildCandidateQueue(
            ["pawtest", "dogtest"],
            ["paw", "dog"],
            "en",
            ["io", "com"]);

        Assert.Equal("com", queue[0].Tld);
        Assert.Equal("com", queue[1].Tld);
    }

    private sealed class FakePlanner(IReadOnlyList<PlannedCheck> checks) : ICheckPlanner
    {
        public Task<IReadOnlyList<PlannedCheck>> PlanAsync(CheckPlannerRequest request, CancellationToken ct = default) =>
            Task.FromResult(checks);
    }

    private sealed class RefillingFakePlanner : ICheckPlanner
    {
        private int _calls;
        public int CallCount => _calls;

        public Task<IReadOnlyList<PlannedCheck>> PlanAsync(CheckPlannerRequest request, CancellationToken ct = default)
        {
            _calls++;
            if (_calls == 1)
            {
                return Task.FromResult<IReadOnlyList<PlannedCheck>>(
                    Enumerable.Range(0, 15)
                        .Select(i => new PlannedCheck($"slot{i}", "com", 80))
                        .ToList());
            }

            var remaining = request.RemainingChecks ?? 10;
            return Task.FromResult<IReadOnlyList<PlannedCheck>>(
                Enumerable.Range(0, remaining)
                    .Select(i => new PlannedCheck($"refill{i}", "io", 75))
                    .ToList());
        }
    }

    private sealed class FakeChecker(
        int availableAfter = int.MaxValue,
        bool credentialsMissing = false,
        bool rateLimited = false) : IDomainAvailabilityChecker
    {
        private int _calls;

        public Task<DomainCheckResult> CheckAsync(string fullDomain, CancellationToken ct = default)
        {
            if (rateLimited)
            {
                return Task.FromResult(new DomainCheckResult(
                    fullDomain, false, null, null, DomainCheckReasons.RateLimited));
            }

            if (credentialsMissing)
            {
                return Task.FromResult(new DomainCheckResult(
                    fullDomain, false, null, null, DomainCheckReasons.CredentialsMissing));
            }

            _calls++;
            var available = _calls >= availableAfter;
            return Task.FromResult(new DomainCheckResult(
                fullDomain,
                available,
                available ? 12.99m : null,
                available ? "standard" : null,
                available ? null : DomainCheckReasons.Unavailable));
        }
    }
}
