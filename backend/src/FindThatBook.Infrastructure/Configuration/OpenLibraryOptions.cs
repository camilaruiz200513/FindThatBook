namespace FindThatBook.Infrastructure.Configuration;

public sealed class OpenLibraryOptions
{
    public const string SectionName = "OpenLibrary";

    public string BaseUrl { get; init; } = "https://openlibrary.org";
    public int TimeoutSeconds { get; init; } = 15;
    public int SearchLimit { get; init; } = 25;
    public int CacheTtlMinutes { get; init; } = 10;
    public string UserAgent { get; init; } = "FindThatBook/1.0";

    // Number of top search results to enrich via /works/{id}.json +
    // /authors/{key}.json for authoritative primary-author resolution.
    // The rest keep the cheaper "first author_name = primary" heuristic.
    public int EnrichTopN { get; init; } = 5;

    // Enable the enricher. Off by default in tests (we stub it), on in the API.
    public bool EnableEnrichment { get; init; } = true;

    // Use /authors/{key}/works.json to expand the candidate pool when the
    // hypothesis is author-only (no title). Off by default to avoid the
    // extra round trip when a direct author search already surfaces the works.
    public bool EnableAuthorWorks { get; init; } = true;
}
