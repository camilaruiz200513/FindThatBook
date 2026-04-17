using System.Globalization;
using System.Text;
using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.OpenLibrary;

public sealed class CachingBookCatalogSource : IBookCatalogSource
{
    private readonly IBookCatalogSource _inner;
    private readonly IMemoryCache _cache;
    private readonly OpenLibraryOptions _options;
    private readonly ITextNormalizer _normalizer;
    private readonly CatalogCacheCoordinator _coordinator;
    private readonly ILogger<CachingBookCatalogSource> _logger;

    public CachingBookCatalogSource(
        IBookCatalogSource inner,
        IMemoryCache cache,
        IOptions<OpenLibraryOptions> options,
        ITextNormalizer normalizer,
        CatalogCacheCoordinator coordinator,
        ILogger<CachingBookCatalogSource> logger)
    {
        _inner = inner;
        _cache = cache;
        _options = options.Value;
        _normalizer = normalizer;
        _coordinator = coordinator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Book>> SearchAsync(ExtractedBookInfo hypothesis, CancellationToken cancellationToken = default)
    {
        if (hypothesis.IsEmpty)
        {
            return Array.Empty<Book>();
        }

        var key = BuildCacheKey(hypothesis);

        if (_cache.TryGetValue<IReadOnlyList<Book>>(key, out var cached) && cached is not null)
        {
            _logger.LogDebug("Open Library cache hit for {Key}", key);
            return cached;
        }

        // Stampede protection: only one concurrent request per key does the
        // fetch; the rest block here, then read from cache on the next line.
        using var _ = await _coordinator.AcquireAsync(key, cancellationToken);

        if (_cache.TryGetValue<IReadOnlyList<Book>>(key, out cached) && cached is not null)
        {
            return cached;
        }

        var result = await _inner.SearchAsync(hypothesis, cancellationToken);

        // Never cache empty results: they're often produced by transient failures
        // (timeouts, 5xx) and we don't want to lock the user out for the full TTL.
        if (result.Count > 0)
        {
            _cache.Set(key, result, TimeSpan.FromMinutes(_options.CacheTtlMinutes));
            _logger.LogDebug("Open Library cache miss → cached {Count} for {Key}", result.Count, key);
        }
        else
        {
            _logger.LogDebug("Open Library returned 0 results for {Key}; not caching.", key);
        }

        return result;
    }

    // Use the same TextNormalizer the matcher relies on so that "García Márquez"
    // and "Garcia Marquez" collide on the same key (they resolve to the same
    // results downstream, caching them separately is waste).
    private string BuildCacheKey(ExtractedBookInfo hypothesis)
    {
        var sb = new StringBuilder("ol::");
        sb.Append(_normalizer.Normalize(hypothesis.Title)).Append('|');
        sb.Append(_normalizer.Normalize(hypothesis.Author)).Append('|');
        sb.Append(hypothesis.Year?.ToString(CultureInfo.InvariantCulture)).Append('|');
        foreach (var keyword in hypothesis.Keywords.OrderBy(k => k, StringComparer.Ordinal))
        {
            sb.Append(_normalizer.Normalize(keyword)).Append(',');
        }
        return sb.ToString();
    }
}
