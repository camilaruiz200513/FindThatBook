using System.Net.Http.Json;
using System.Text.Json;
using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.OpenLibrary;

/// <summary>
/// Enriches the top-N candidates by calling <c>/works/{id}.json</c> to get the
/// authoritative author list, then <c>/authors/{key}.json</c> for each author
/// key to resolve the canonical name. Everything is cached per-key with the
/// same TTL as search results, so repeated queries are cheap.
/// </summary>
public sealed class OpenLibraryBookEnricher : IBookEnricher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly OpenLibraryOptions _options;
    private readonly ILogger<OpenLibraryBookEnricher> _logger;

    public OpenLibraryBookEnricher(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<OpenLibraryOptions> options,
        ILogger<OpenLibraryBookEnricher> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Book>> EnrichAsync(IReadOnlyList<Book> books, CancellationToken cancellationToken = default)
    {
        if (books.Count == 0)
        {
            return books;
        }

        var limit = Math.Min(books.Count, _options.EnrichTopN);
        var enriched = new List<Book>(books.Count);

        for (var i = 0; i < books.Count; i++)
        {
            if (i >= limit)
            {
                enriched.Add(books[i]);
                continue;
            }

            try
            {
                enriched.Add(await EnrichOneAsync(books[i], cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Enrichment failed for {WorkId}; falling back to search-level data.", books[i].WorkId);
                enriched.Add(books[i]);
            }
        }

        return enriched;
    }

    private async Task<Book> EnrichOneAsync(Book book, CancellationToken ct)
    {
        var work = await GetWorkAsync(book.WorkId, ct);
        if (work?.Authors is null || work.Authors.Count == 0)
        {
            return book;
        }

        // Resolve each author key to a canonical name. Order is preserved: the
        // first entry in work.authors is the primary author in Open Library's
        // data model, the rest are co-authors (all still "primary" in the
        // literary sense). Any author_name from /search.json that does NOT
        // appear in this canonical list is reclassified as a contributor.
        var canonical = new List<string>(work.Authors.Count);
        foreach (var entry in work.Authors)
        {
            var key = entry.Author?.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }
            var name = await GetAuthorNameAsync(key, ct);
            if (!string.IsNullOrWhiteSpace(name))
            {
                canonical.Add(name);
            }
        }

        if (canonical.Count == 0)
        {
            return book;
        }

        var contributors = book.PrimaryAuthors
            .Concat(book.Contributors)
            .Where(n => !canonical.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return book with
        {
            PrimaryAuthors = canonical,
            Contributors = contributors,
        };
    }

    private Task<OpenLibraryWork?> GetWorkAsync(string workKey, CancellationToken ct)
    {
        var cacheKey = $"ol-work::{workKey}";
        return GetOrFetchAsync<OpenLibraryWork>(cacheKey, $"{workKey}.json", ct);
    }

    private async Task<string?> GetAuthorNameAsync(string authorKey, CancellationToken ct)
    {
        var cacheKey = $"ol-author::{authorKey}";
        var author = await GetOrFetchAsync<OpenLibraryAuthor>(cacheKey, $"{authorKey}.json", ct);
        return author?.Name ?? author?.PersonalName;
    }

    private async Task<T?> GetOrFetchAsync<T>(string cacheKey, string relativeUrl, CancellationToken ct)
        where T : class
    {
        if (_cache.TryGetValue<T>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var response = await _httpClient.GetAsync(relativeUrl, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Open Library {Url} returned {Status}", relativeUrl, (int)response.StatusCode);
            return null;
        }

        var parsed = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        if (parsed is not null)
        {
            _cache.Set(cacheKey, parsed, TimeSpan.FromMinutes(_options.CacheTtlMinutes));
        }
        return parsed;
    }
}
