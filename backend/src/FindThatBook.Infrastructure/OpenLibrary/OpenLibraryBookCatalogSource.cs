using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.OpenLibrary;

public sealed class OpenLibraryBookCatalogSource : IBookCatalogSource
{
    internal const string HttpClientName = "OpenLibrary";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const string SearchFields =
        "key,title,author_name,first_publish_year,cover_i,isbn,subject";

    private readonly HttpClient _httpClient;
    private readonly OpenLibraryOptions _options;
    private readonly ILogger<OpenLibraryBookCatalogSource> _logger;

    public OpenLibraryBookCatalogSource(
        HttpClient httpClient,
        IOptions<OpenLibraryOptions> options,
        ILogger<OpenLibraryBookCatalogSource> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Book>> SearchAsync(ExtractedBookInfo hypothesis, CancellationToken cancellationToken = default)
    {
        if (hypothesis.IsEmpty)
        {
            return Array.Empty<Book>();
        }

        var url = BuildSearchUrl(hypothesis);
        _logger.LogDebug("Open Library request: {Url}", url);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Open Library returned {Status} for {Url}",
                    (int)response.StatusCode, url);
                return Array.Empty<Book>();
            }

            var parsed = await response.Content.ReadFromJsonAsync<OpenLibrarySearchResponse>(JsonOptions, cancellationToken);
            return MapDocs(parsed?.Docs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open Library search failed.");
            return Array.Empty<Book>();
        }
    }

    private string BuildSearchUrl(ExtractedBookInfo hypothesis)
    {
        var query = new StringBuilder("/search.json?");
        var hasParam = false;

        void Append(string key, string value)
        {
            if (hasParam) { query.Append('&'); }
            query.Append(key).Append('=').Append(Uri.EscapeDataString(value));
            hasParam = true;
        }

        if (hypothesis.HasTitle) { Append("title", hypothesis.Title!); }
        if (hypothesis.HasAuthor) { Append("author", hypothesis.Author!); }
        if (hypothesis.Year is { } year) { Append("first_publish_year", year.ToString()); }
        if (!hasParam)
        {
            var keywords = string.Join(' ', hypothesis.Keywords);
            Append("q", keywords);
        }

        Append("limit", _options.SearchLimit.ToString());
        Append("fields", SearchFields);
        return query.ToString();
    }

    private static IReadOnlyList<Book> MapDocs(List<OpenLibraryDoc>? docs)
    {
        if (docs is null || docs.Count == 0)
        {
            return Array.Empty<Book>();
        }

        var books = new List<Book>(docs.Count);
        foreach (var doc in docs)
        {
            if (string.IsNullOrWhiteSpace(doc.Key) || string.IsNullOrWhiteSpace(doc.Title))
            {
                continue;
            }

            // Heuristic: Open Library /search.json does not distinguish primary authors from
            // contributors. As a pragmatic approximation, the first author in the list is treated
            // as primary; the rest are treated as contributors. This is documented in the README.
            var authorList = doc.AuthorName ?? new List<string>();
            var primary = authorList.Count > 0
                ? new[] { authorList[0] }
                : Array.Empty<string>();
            var contributors = authorList.Count > 1
                ? authorList.Skip(1).ToArray()
                : Array.Empty<string>();

            books.Add(new Book(
                WorkId: doc.Key!,
                Title: doc.Title!,
                PrimaryAuthors: primary,
                Contributors: contributors,
                FirstPublishYear: doc.FirstPublishYear,
                CoverId: doc.CoverId?.ToString(),
                Subjects: doc.Subject ?? new List<string>(),
                Isbns: doc.Isbn ?? new List<string>()));
        }

        return books;
    }
}
