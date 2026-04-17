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
/// Two-step resolution: <c>/search/authors.json?q=…</c> to find the author key,
/// then <c>/authors/{key}/works.json</c> for the canonical works list. Used in
/// the author-only fallback path to widen the candidate pool beyond what a
/// plain keyword <c>/search.json?author=…</c> returns.
/// </summary>
public sealed class OpenLibraryAuthorWorksSource : IAuthorWorksSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly OpenLibraryOptions _options;
    private readonly ILogger<OpenLibraryAuthorWorksSource> _logger;

    public OpenLibraryAuthorWorksSource(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<OpenLibraryOptions> options,
        ILogger<OpenLibraryAuthorWorksSource> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Book>> FetchByAuthorNameAsync(string authorName, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorName) || limit <= 0)
        {
            return Array.Empty<Book>();
        }

        try
        {
            var key = await ResolveAuthorKeyAsync(authorName, cancellationToken);
            if (string.IsNullOrWhiteSpace(key))
            {
                return Array.Empty<Book>();
            }

            var works = await FetchWorksAsync(key, limit, cancellationToken);
            if (works is null || works.Count == 0)
            {
                return Array.Empty<Book>();
            }

            var authorNameCanonical = await ResolveAuthorNameAsync(key, cancellationToken) ?? authorName;
            return works
                .Where(w => !string.IsNullOrWhiteSpace(w.Key) && !string.IsNullOrWhiteSpace(w.Title))
                .Take(limit)
                .Select(w => new Book(
                    WorkId: w.Key!,
                    Title: w.Title!,
                    PrimaryAuthors: new[] { authorNameCanonical },
                    Contributors: Array.Empty<string>(),
                    FirstPublishYear: TryParseYear(w.FirstPublishDate),
                    CoverId: w.Covers?.FirstOrDefault().ToString(),
                    Subjects: Array.Empty<string>(),
                    Isbns: Array.Empty<string>()))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Author-works lookup failed for '{Author}'.", authorName);
            return Array.Empty<Book>();
        }
    }

    private async Task<string?> ResolveAuthorKeyAsync(string name, CancellationToken ct)
    {
        var cacheKey = $"ol-author-key::{name.ToLowerInvariant()}";
        if (_cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        var url = $"/search/authors.json?q={Uri.EscapeDataString(name)}&limit=1";
        var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Open Library {Url} returned {Status}", url, (int)response.StatusCode);
            return null;
        }

        var parsed = await response.Content.ReadFromJsonAsync<OpenLibraryAuthorSearchResponse>(JsonOptions, ct);
        var key = parsed?.Docs?.FirstOrDefault()?.Key;
        if (!string.IsNullOrWhiteSpace(key))
        {
            _cache.Set(cacheKey, key, TimeSpan.FromMinutes(_options.CacheTtlMinutes));
        }
        return key;
    }

    private async Task<List<OpenLibraryAuthorWorkEntry>?> FetchWorksAsync(string authorKey, int limit, CancellationToken ct)
    {
        var cacheKey = $"ol-author-works::{authorKey}::{limit}";
        if (_cache.TryGetValue<List<OpenLibraryAuthorWorkEntry>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var url = $"{authorKey}/works.json?limit={limit}";
        var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Open Library {Url} returned {Status}", url, (int)response.StatusCode);
            return null;
        }

        var parsed = await response.Content.ReadFromJsonAsync<OpenLibraryAuthorWorks>(JsonOptions, ct);
        var entries = parsed?.Entries;
        if (entries is not null)
        {
            _cache.Set(cacheKey, entries, TimeSpan.FromMinutes(_options.CacheTtlMinutes));
        }
        return entries;
    }

    private async Task<string?> ResolveAuthorNameAsync(string authorKey, CancellationToken ct)
    {
        var cacheKey = $"ol-author::{authorKey}";
        if (_cache.TryGetValue<OpenLibraryAuthor>(cacheKey, out var cached) && cached is not null)
        {
            return cached.Name ?? cached.PersonalName;
        }

        var url = $"{authorKey}.json";
        var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var parsed = await response.Content.ReadFromJsonAsync<OpenLibraryAuthor>(JsonOptions, ct);
        if (parsed is not null)
        {
            _cache.Set(cacheKey, parsed, TimeSpan.FromMinutes(_options.CacheTtlMinutes));
        }
        return parsed?.Name ?? parsed?.PersonalName;
    }

    private static int? TryParseYear(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // first_publish_date may be "1884", "June 1884", "1884-06-01", etc.
        for (var i = 0; i + 4 <= raw.Length; i++)
        {
            if (int.TryParse(raw.AsSpan(i, 4), out var year) && year > 1000 && year < 3000)
            {
                return year;
            }
        }
        return null;
    }
}

internal sealed class OpenLibraryAuthorSearchResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("docs")]
    public List<OpenLibraryAuthorSearchDoc>? Docs { get; set; }
}

internal sealed class OpenLibraryAuthorSearchDoc
{
    [System.Text.Json.Serialization.JsonPropertyName("key")]
    public string? Key { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }
}
