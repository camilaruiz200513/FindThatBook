using FindThatBook.Core.Domain;
using FindThatBook.Core.Matching;
using FindThatBook.Core.Models;
using FindThatBook.Core.Ports;
using FindThatBook.Core.UseCases;
using FindThatBook.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FindThatBook.Tests.Core.UseCases;

public class FindBookQueryHandlerTests
{
    private static FindBookQueryHandler CreateHandler(
        ILlmService llm,
        IBookCatalogSource catalog,
        IBookMatcher matcher,
        IBookEnricher? enricher = null,
        IAuthorWorksSource? authorWorks = null,
        MatchingOptions? options = null) =>
        new(
            llm,
            catalog,
            enricher ?? new NoOpBookEnricher(),
            authorWorks ?? new NoOpAuthorWorksSource(),
            matcher,
            Options.Create(options ?? new MatchingOptions { UseLlmRerank = false }),
            NullLogger<FindBookQueryHandler>.Instance);

    [Fact]
    public async Task HandleAsync_orchestrates_extraction_catalog_search_and_matching()
    {
        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", 1937, Array.Empty<string>());
        var books = new[] { BookFactory.Create() };
        var expected = new[]
        {
            new BookCandidate(books[0], MatchTier.Exact, "test", "explanation"),
        };

        var llm = new Mock<ILlmService>();
        llm.Setup(x => x.ExtractAsync("mark huckleberry", It.IsAny<CancellationToken>()))
            .ReturnsAsync(hypothesis);

        var catalog = new Mock<IBookCatalogSource>();
        catalog.Setup(x => x.SearchAsync(hypothesis, It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);

        var matcher = new Mock<IBookMatcher>();
        matcher.Setup(x => x.Rank(It.IsAny<IReadOnlyList<Book>>(), hypothesis, 5)).Returns(expected);

        var handler = CreateHandler(llm.Object, catalog.Object, matcher.Object);

        var response = await handler.HandleAsync(new FindBookRequest("mark huckleberry", 5));

        response.OriginalQuery.Should().Be("mark huckleberry");
        response.Hypothesis.Should().Be(hypothesis);
        response.Candidates.Should().BeEquivalentTo(expected);
        response.TotalCandidates.Should().Be(1);
        response.ProcessingTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task HandleAsync_returns_empty_response_when_extraction_is_empty()
    {
        var llm = new Mock<ILlmService>();
        llm.Setup(x => x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExtractedBookInfo.Empty);

        var catalog = new Mock<IBookCatalogSource>();
        catalog.Setup(x => x.SearchAsync(ExtractedBookInfo.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Book>());

        var matcher = new Mock<IBookMatcher>();
        matcher.Setup(x => x.Rank(It.IsAny<IReadOnlyList<Book>>(), It.IsAny<ExtractedBookInfo>(), It.IsAny<int>()))
            .Returns(Array.Empty<BookCandidate>());

        var handler = CreateHandler(llm.Object, catalog.Object, matcher.Object);

        var response = await handler.HandleAsync(new FindBookRequest("asdf"));

        response.TotalCandidates.Should().Be(0);
        response.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_calls_enricher_before_matching()
    {
        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", 1937, Array.Empty<string>());
        var rawBooks = new[] { BookFactory.Create(primary: new[] { "Tolkien" }) };
        var enrichedBooks = new[] { BookFactory.Create(primary: new[] { "J.R.R. Tolkien" }) };

        var llm = new Mock<ILlmService>();
        llm.Setup(x => x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(hypothesis);

        var catalog = new Mock<IBookCatalogSource>();
        catalog.Setup(x => x.SearchAsync(hypothesis, It.IsAny<CancellationToken>())).ReturnsAsync(rawBooks);

        var enricher = new Mock<IBookEnricher>();
        enricher.Setup(x => x.EnrichAsync(rawBooks, It.IsAny<CancellationToken>())).ReturnsAsync(enrichedBooks);

        var matcher = new Mock<IBookMatcher>();
        matcher.Setup(x => x.Rank(enrichedBooks, hypothesis, 5))
            .Returns(new[] { new BookCandidate(enrichedBooks[0], MatchTier.Exact, "rule", "why") });

        var handler = CreateHandler(llm.Object, catalog.Object, matcher.Object, enricher: enricher.Object);

        var response = await handler.HandleAsync(new FindBookRequest("anything"));

        response.Candidates.Should().ContainSingle().Which.Book.PrimaryAuthors.Should().Contain("J.R.R. Tolkien");
        enricher.Verify(x => x.EnrichAsync(rawBooks, It.IsAny<CancellationToken>()), Times.Once);
        matcher.Verify(x => x.Rank(enrichedBooks, hypothesis, 5), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_fetches_author_works_when_hypothesis_is_author_only()
    {
        var hypothesis = new ExtractedBookInfo(null, "Jane Austen", null, Array.Empty<string>());
        var searchBooks = new[] { BookFactory.Create(title: "Pride and Prejudice", workId: "/works/OL1") };
        var authorWorks = new[] { BookFactory.Create(title: "Emma", workId: "/works/OL2") };

        var llm = new Mock<ILlmService>();
        llm.Setup(x => x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(hypothesis);

        var catalog = new Mock<IBookCatalogSource>();
        catalog.Setup(x => x.SearchAsync(hypothesis, It.IsAny<CancellationToken>())).ReturnsAsync(searchBooks);

        var works = new Mock<IAuthorWorksSource>();
        works.Setup(x => x.FetchByAuthorNameAsync("Jane Austen", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authorWorks);

        IReadOnlyList<Book>? rankedInput = null;
        var matcher = new Mock<IBookMatcher>();
        matcher.Setup(x => x.Rank(It.IsAny<IReadOnlyList<Book>>(), hypothesis, It.IsAny<int>()))
            .Callback<IReadOnlyList<Book>, ExtractedBookInfo, int>((b, _, _) => rankedInput = b)
            .Returns(Array.Empty<BookCandidate>());

        var handler = CreateHandler(llm.Object, catalog.Object, matcher.Object, authorWorks: works.Object);

        await handler.HandleAsync(new FindBookRequest("austen"));

        works.Verify(x => x.FetchByAuthorNameAsync("Jane Austen", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        rankedInput.Should().NotBeNull();
        rankedInput!.Select(b => b.WorkId).Should().BeEquivalentTo(new[] { "/works/OL1", "/works/OL2" });
    }

    [Fact]
    public async Task HandleAsync_reorders_candidates_with_llm_rerank_when_enabled()
    {
        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", null, Array.Empty<string>());
        var b1 = BookFactory.Create(title: "A", workId: "/works/OL-A");
        var b2 = BookFactory.Create(title: "B", workId: "/works/OL-B");
        var ranked = new[]
        {
            new BookCandidate(b1, MatchTier.Good, "rule", "why A"),
            new BookCandidate(b2, MatchTier.Good, "rule", "why B"),
        };

        var llm = new Mock<ILlmService>();
        llm.Setup(x => x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(hypothesis);
        llm.Setup(x => x.RerankAsync(It.IsAny<string>(), hypothesis, It.IsAny<IReadOnlyList<BookCandidate>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "/works/OL-B", "/works/OL-A" });

        var catalog = new Mock<IBookCatalogSource>();
        catalog.Setup(x => x.SearchAsync(hypothesis, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { b1, b2 });

        var matcher = new Mock<IBookMatcher>();
        matcher.Setup(x => x.Rank(It.IsAny<IReadOnlyList<Book>>(), hypothesis, It.IsAny<int>())).Returns(ranked);

        var handler = CreateHandler(
            llm.Object, catalog.Object, matcher.Object,
            options: new MatchingOptions { UseLlmRerank = true, RerankTopK = 5 });

        var response = await handler.HandleAsync(new FindBookRequest("anything"));

        response.Candidates.Select(c => c.Book.WorkId).Should().ContainInOrder("/works/OL-B", "/works/OL-A");
    }
}
