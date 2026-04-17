using System.Net;
using FindThatBook.Infrastructure.Configuration;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests.Infrastructure.OpenLibrary;

public class OpenLibraryAuthorWorksSourceTests
{
    private static OpenLibraryAuthorWorksSource CreateSut(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openlibrary.org") },
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new OpenLibraryOptions()),
            NullLogger<OpenLibraryAuthorWorksSource>.Instance);

    [Fact]
    public async Task FetchByAuthorNameAsync_resolves_author_key_then_fetches_canonical_works()
    {
        const string authorSearch = """
        { "docs": [ { "key": "/authors/OL26320A", "name": "J.R.R. Tolkien" } ] }
        """;
        const string works = """
        {
          "entries": [
            { "key": "/works/OL262758W", "title": "The Hobbit", "first_publish_date": "1937", "covers": [1] },
            { "key": "/works/OL27482W", "title": "The Lord of the Rings", "first_publish_date": "1954", "covers": [2] }
          ]
        }
        """;
        const string author = """
        { "name": "J.R.R. Tolkien" }
        """;

        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/search/authors.json" => new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(authorSearch, System.Text.Encoding.UTF8, "application/json") },
                "/authors/OL26320A/works.json" => new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(works, System.Text.Encoding.UTF8, "application/json") },
                "/authors/OL26320A.json" => new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(author, System.Text.Encoding.UTF8, "application/json") },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        });

        var sut = CreateSut(handler);

        var books = await sut.FetchByAuthorNameAsync("Tolkien", 10);

        books.Should().HaveCount(2);
        books[0].Title.Should().Be("The Hobbit");
        books[0].FirstPublishYear.Should().Be(1937);
        books[0].PrimaryAuthors.Should().ContainSingle().Which.Should().Be("J.R.R. Tolkien");
        books[1].Title.Should().Be("The Lord of the Rings");
    }

    [Fact]
    public async Task FetchByAuthorNameAsync_returns_empty_when_author_key_cannot_be_resolved()
    {
        var handler = StubHttpMessageHandler.Constant(HttpStatusCode.OK, """{ "docs": [] }""");
        var sut = CreateSut(handler);

        var result = await sut.FetchByAuthorNameAsync("Unknown Author", 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchByAuthorNameAsync_returns_empty_for_blank_input()
    {
        var handler = StubHttpMessageHandler.Constant(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        var result = await sut.FetchByAuthorNameAsync("", 10);

        result.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }
}
