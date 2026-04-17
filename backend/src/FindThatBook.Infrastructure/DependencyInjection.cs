using FindThatBook.Core.Matching;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Configuration;
using FindThatBook.Infrastructure.Gemini;
using FindThatBook.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<OpenLibraryOptions>(configuration.GetSection(OpenLibraryOptions.SectionName));

        services.AddMemoryCache();

        services.AddHttpClient<ILlmService, GeminiLlmService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
            // Delegate timeout to the resilience handler's TotalRequestTimeout below;
            // a tight HttpClient.Timeout would abort retries before they can complete.
            client.Timeout = Timeout.InfiniteTimeSpan;
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Add("x-goog-api-key", options.ApiKey);
            }
        })
        .AddStandardResilienceHandler(o =>
        {
            o.Retry.MaxRetryAttempts = 3;
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
        });

        // Shared HttpClient configuration for all Open Library calls.
        static void ConfigureOpenLibraryClient(IServiceProvider sp, HttpClient client)
        {
            var options = sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        }

        static void ConfigureOpenLibraryResilience(HttpStandardResilienceOptions o)
        {
            o.Retry.MaxRetryAttempts = 3;
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(45);
        }

        services.AddHttpClient<OpenLibraryBookCatalogSource>(ConfigureOpenLibraryClient)
            .AddStandardResilienceHandler(ConfigureOpenLibraryResilience);

        services.AddHttpClient<OpenLibraryBookEnricher>(ConfigureOpenLibraryClient)
            .AddStandardResilienceHandler(ConfigureOpenLibraryResilience);

        services.AddHttpClient<OpenLibraryAuthorWorksSource>(ConfigureOpenLibraryClient)
            .AddStandardResilienceHandler(ConfigureOpenLibraryResilience);

        services.AddSingleton<CatalogCacheCoordinator>();

        services.AddTransient<IBookCatalogSource>(sp =>
        {
            var inner = sp.GetRequiredService<OpenLibraryBookCatalogSource>();
            var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var options = sp.GetRequiredService<IOptions<OpenLibraryOptions>>();
            var normalizer = sp.GetRequiredService<Core.Ports.ITextNormalizer>();
            var coordinator = sp.GetRequiredService<CatalogCacheCoordinator>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachingBookCatalogSource>>();
            return new CachingBookCatalogSource(inner, cache, options, normalizer, coordinator, logger);
        });

        // Enricher: real implementation only when enabled; otherwise a no-op
        // keeps the handler code uniform.
        services.AddTransient<IBookEnricher>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value;
            return options.EnableEnrichment
                ? sp.GetRequiredService<OpenLibraryBookEnricher>()
                : new NoOpBookEnricher();
        });

        services.AddTransient<IAuthorWorksSource>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<OpenLibraryOptions>>().Value;
            return options.EnableAuthorWorks
                ? sp.GetRequiredService<OpenLibraryAuthorWorksSource>()
                : new NoOpAuthorWorksSource();
        });

        return services;
    }
}
