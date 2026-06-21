using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SeoDomainFinder.Core.Abstractions;
using SeoDomainFinder.Core.Generators;
using SeoDomainFinder.Core.Scoring;
using SeoDomainFinder.Core.Services;
using SeoDomainFinder.Infrastructure.OpenRouter;
using SeoDomainFinder.Infrastructure.Options;
using SeoDomainFinder.Infrastructure.Porkbun;
using SeoDomainFinder.Infrastructure.RateLimiting;

namespace SeoDomainFinder.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSeoDomainFinderInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PorkbunOptions>(configuration.GetSection(PorkbunOptions.SectionName));
        services.Configure<OpenRouterOptions>(configuration.GetSection(OpenRouterOptions.SectionName));
        services.Configure<DemoRateLimitOptions>(configuration.GetSection(DemoRateLimitOptions.SectionName));
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<DemoRateLimiter>();

        services.AddHttpClient("Porkbun", c =>
        {
            c.BaseAddress = new Uri("https://api.porkbun.com/api/json/v3/");
            c.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient("OpenRouter", c =>
        {
            c.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
            c.Timeout = TimeSpan.FromSeconds(120);
            c.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/seo-domain-finder");
            c.DefaultRequestHeaders.Add("X-Title", "SEO Domain Finder");
        });

        services.AddSingleton<ISeoScorer, SeoScorer>();
        services.AddSingleton<INameGenerator, HeuristicNameGenerator>();
        services.AddSingleton<INameGenerator, OpenRouterNameGenerator>();
        services.AddSingleton<ICheckPlanner, OpenRouterCheckPlanner>();
        services.AddSingleton<IDomainAdvisor, OpenRouterAdvisor>();
        services.AddSingleton<PorkbunDomainChecker>();
        services.AddSingleton<IDomainAvailabilityChecker>(sp => sp.GetRequiredService<PorkbunDomainChecker>());
        services.AddSingleton<IDomainSearchService, DomainSearchService>();

        return services;
    }
}
