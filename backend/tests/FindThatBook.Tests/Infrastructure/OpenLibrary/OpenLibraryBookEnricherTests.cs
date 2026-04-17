using System.Net;
using FindThatBook.Core.Domain;
using FindThatBook.Infrastructure.Configuration;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests.Infrastructure.OpenLibrary;

public class OpenLibraryBookEnricherTests
{
    private static OpenLibraryBookEnricher CreateSut(
        HttpMessageHandler handler,
        OpenLibraryOptions? options = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openlibrary.org") },
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options ?? new OpenLibraryOptions { EnrichTopN = 5 }),
            NullLogger<OpenLibraryBookEnricher>.Instance);

    [Fact]
    public async Task EnrichAsync_reassigns_contributor_to_primary_when_works_endpoint_disagrees_with_search()
    {
        // /search.json put "Dixon" as first author_name (heuristic → primary),
        // but /works/{id}.json says Tolkien is the authoritative primary.
        // Enricher must swap them.
        var work = """
        {
          "title": "The Hobbit",
          "authors": [{ "author": { "key": "/authors/OL26320A" } }]
        }
        """;
        var tolkien = """
        { "name": "J.R.R. Tolkien" }
        """;

        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/works/OL262758W.json" => new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(work, System.Text.Encoding.UTF8, "application/json") },
                "/authors/OL26320A.json" => new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(tolkien, System.Text.Encoding.UTF8, "application/json") },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        });

        var sut = CreateSut(handler);

        var input = new[]
        {
            BookFactory.Create(
                workId: "/works/OL262758W",
                title: "The Hobbit",
                primary: new[] { "Dixon" },           // search.json heuristic got it wrong
                contributors: new[] { "J.R.R. Tolkien" }),
        };

        var enriched = await sut.EnrichAsync(input);

        enriched.Should().ContainSingle();
        enriched[0].PrimaryAuthors.Should().ContainSingle().Which.Should().Be("J.R.R. Tolkien");
        enriched[0].Contributors.Should().Contain("Dixon");
    }

    [Fact]
    public async Task EnrichAsync_passes_through_when_works_endpoint_fails()
    {
        var handler = StubHttpMessageHandler.Constant(HttpStatusCode.InternalServerError, "boom");
        var sut = CreateSut(handler);

        var input = new[] { BookFactory.Create() };

        var enriched = await sut.EnrichAsync(input);

        enriched.Should().BeEquivalentTo(input);
    }

    [Fact]
    public async Task EnrichAsync_only_touches_top_N()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var sut = CreateSut(handler, new OpenLibraryOptions { EnrichTopN = 2 });

        var input = Enumerable.Range(0, 5)
            .Select(i => BookFactory.Create(workId: $"/works/OL{i}W"))
            .ToArray();

        await sut.EnrichAsync(input);

        // EnrichTopN=2 → at most 2 /works/{id}.json calls are attempted.
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath.StartsWith("/works/"))
            .Should().BeLessThanOrEqualTo(2);
    }
}
