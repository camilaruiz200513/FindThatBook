using System.Net;
using FindThatBook.Core.Domain;
using FindThatBook.Infrastructure.Configuration;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests.Infrastructure.OpenLibrary;

public class OpenLibraryBookCatalogSourceTests
{
    private static OpenLibraryBookCatalogSource CreateSut(StubHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openlibrary.org") },
            Options.Create(new OpenLibraryOptions()),
            NullLogger<OpenLibraryBookCatalogSource>.Instance);

    [Fact]
    public async Task SearchAsync_maps_first_author_to_primary_and_rest_to_contributors()
    {
        const string body = """
        {
          "numFound": 1,
          "docs": [
            {
              "key": "/works/OL1W",
              "title": "The Hobbit",
              "author_name": ["J.R.R. Tolkien", "Alan Lee"],
              "first_publish_year": 1937,
              "cover_i": 10001,
              "isbn": ["0-618-00221-9"],
              "subject": ["Fantasy"]
            }
          ]
        }
        """;
        var handler = StubHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var sut = CreateSut(handler);

        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", 1937, Array.Empty<string>());

        var books = await sut.SearchAsync(hypothesis);

        books.Should().ContainSingle();
        var book = books[0];
        book.WorkId.Should().Be("/works/OL1W");
        book.PrimaryAuthors.Should().ContainSingle().Which.Should().Be("J.R.R. Tolkien");
        book.Contributors.Should().ContainSingle().Which.Should().Be("Alan Lee");
        book.CoverId.Should().Be("10001");
        book.FirstPublishYear.Should().Be(1937);
        book.OpenLibraryUrl.Should().Be("https://openlibrary.org/works/OL1W");
    }

    [Fact]
    public async Task SearchAsync_returns_empty_on_http_failure()
    {
        var handler = StubHttpMessageHandler.Constant(HttpStatusCode.InternalServerError, "boom");
        var sut = CreateSut(handler);

        var result = await sut.SearchAsync(new ExtractedBookInfo("x", null, null, Array.Empty<string>()));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_returns_empty_for_empty_hypothesis()
    {
        var handler = StubHttpMessageHandler.Constant(HttpStatusCode.OK, "{}");
        var sut = CreateSut(handler);

        var result = await sut.SearchAsync(ExtractedBookInfo.Empty);

        result.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_builds_query_with_title_and_author_parameters()
    {
        var handler = StubHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"numFound\":0,\"docs\":[]}");
        var sut = CreateSut(handler);

        await sut.SearchAsync(new ExtractedBookInfo("The Hobbit", "Tolkien", null, Array.Empty<string>()));

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.RequestUri!.AbsolutePath.Should().Be("/search.json");
        request.RequestUri.Query.Should().Contain("title=").And.Contain("author=").And.Contain("limit=");
    }
}
