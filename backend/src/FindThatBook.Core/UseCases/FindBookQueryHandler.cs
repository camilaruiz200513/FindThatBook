using System.Diagnostics;
using FindThatBook.Core.Domain;
using FindThatBook.Core.Models;
using FindThatBook.Core.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindThatBook.Core.UseCases;

public sealed class FindBookQueryHandler
{
    internal static readonly ActivitySource ActivitySource = new("FindThatBook.Matching");

    private readonly ILlmService _llm;
    private readonly IBookCatalogSource _catalog;
    private readonly IBookEnricher _enricher;
    private readonly IAuthorWorksSource _authorWorks;
    private readonly IBookMatcher _matcher;
    private readonly MatchingOptions _options;
    private readonly ILogger<FindBookQueryHandler> _logger;

    public FindBookQueryHandler(
        ILlmService llm,
        IBookCatalogSource catalog,
        IBookEnricher enricher,
        IAuthorWorksSource authorWorks,
        IBookMatcher matcher,
        IOptions<MatchingOptions> options,
        ILogger<FindBookQueryHandler> logger)
    {
        _llm = llm;
        _catalog = catalog;
        _enricher = enricher;
        _authorWorks = authorWorks;
        _matcher = matcher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FindBookResponse> HandleAsync(FindBookRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("FindBook", ActivityKind.Internal);
        activity?.SetTag("query.length", request.Query.Length);
        activity?.SetTag("query.max_results", request.MaxResults);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Extracting hypothesis for query: {Query}", request.Query);
        var hypothesis = await _llm.ExtractAsync(request.Query, cancellationToken);
        activity?.SetTag("hypothesis.has_title", hypothesis.HasTitle);
        activity?.SetTag("hypothesis.has_author", hypothesis.HasAuthor);
        activity?.SetTag("hypothesis.has_year", hypothesis.Year.HasValue);

        _logger.LogInformation(
            "Hypothesis: Title='{Title}', Author='{Author}', Year={Year}, Keywords=[{Keywords}]",
            hypothesis.Title, hypothesis.Author, hypothesis.Year, string.Join(", ", hypothesis.Keywords));

        var books = await _catalog.SearchAsync(hypothesis, cancellationToken);
        _logger.LogInformation("Catalog returned {Count} candidates", books.Count);

        // Author-only hypothesis: supplement the keyword-based /search.json
        // pool with the author's canonical bibliography. Even if the keyword
        // search found things, the canonical list may surface relevant works
        // the search missed due to ranking quirks.
        if (hypothesis.HasAuthor && !hypothesis.HasTitle && _options.AuthorWorksFallbackLimit > 0)
        {
            var canonical = await _authorWorks.FetchByAuthorNameAsync(
                hypothesis.Author!, _options.AuthorWorksFallbackLimit, cancellationToken);
            if (canonical.Count > 0)
            {
                _logger.LogInformation("Author-works expansion added {Count} canonical works", canonical.Count);
                books = MergeUnique(books, canonical);
            }
        }

        // Enrichment: pull the authoritative primary-vs-contributor split for
        // the top-N via /works/{id}.json + /authors/{key}.json. Implementations
        // are free to no-op (see NoOpBookEnricher) if the round trips aren't
        // worth the latency.
        books = await _enricher.EnrichAsync(books, cancellationToken);

        var ranked = _matcher.Rank(books, hypothesis, request.MaxResults);
        activity?.SetTag("matcher.candidates", ranked.Count);

        if (_options.UseLlmRerank && ranked.Count > 1)
        {
            ranked = await ApplyLlmRerankAsync(request.Query, hypothesis, ranked, cancellationToken);
        }

        stopwatch.Stop();
        activity?.SetTag("elapsed_ms", stopwatch.ElapsedMilliseconds);
        _logger.LogInformation(
            "Match complete: {Count} ranked candidates in {Elapsed}ms",
            ranked.Count, stopwatch.ElapsedMilliseconds);

        return new FindBookResponse(
            request.Query,
            hypothesis,
            ranked,
            ranked.Count,
            stopwatch.Elapsed);
    }

    private async Task<IReadOnlyList<BookCandidate>> ApplyLlmRerankAsync(
        string query,
        ExtractedBookInfo hypothesis,
        IReadOnlyList<BookCandidate> ranked,
        CancellationToken ct)
    {
        var topK = ranked.Take(_options.RerankTopK).ToArray();
        if (topK.Length < 2)
        {
            return ranked;
        }

        var order = await _llm.RerankAsync(query, hypothesis, topK, ct);
        if (order.Count == 0)
        {
            return ranked;
        }

        var byId = topK.ToDictionary(c => c.Book.WorkId, c => c);
        var reordered = new List<BookCandidate>(ranked.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Respect the LLM's ordering for ids it mentioned; then append any
        // top-K ids it omitted (in their original order); then append the
        // tail candidates (those outside top-K) untouched.
        foreach (var id in order)
        {
            if (byId.TryGetValue(id, out var candidate) && seen.Add(id))
            {
                reordered.Add(candidate);
            }
        }
        foreach (var candidate in topK)
        {
            if (seen.Add(candidate.Book.WorkId))
            {
                reordered.Add(candidate);
            }
        }
        foreach (var candidate in ranked.Skip(_options.RerankTopK))
        {
            reordered.Add(candidate);
        }
        return reordered;
    }

    private static IReadOnlyList<Book> MergeUnique(IReadOnlyList<Book> primary, IReadOnlyList<Book> extra)
    {
        var seen = new HashSet<string>(primary.Select(b => b.WorkId), StringComparer.Ordinal);
        var merged = new List<Book>(primary);
        foreach (var book in extra)
        {
            if (seen.Add(book.WorkId))
            {
                merged.Add(book);
            }
        }
        return merged;
    }
}
