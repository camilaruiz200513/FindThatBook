using FindThatBook.Core.Domain;
using FindThatBook.Core.Matching;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Configuration;
using FindThatBook.Infrastructure.OpenLibrary;
using FindThatBook.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FindThatBook.Tests.Infrastructure.OpenLibrary;

public class CachingBookCatalogSourceTests
{
    private static CachingBookCatalogSource CreateSut(
        IBookCatalogSource inner,
        IMemoryCache? cache = null,
        ITextNormalizer? normalizer = null) =>
        new(
            inner,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new OpenLibraryOptions { CacheTtlMinutes = 10 }),
            normalizer ?? new TextNormalizer(),
            new FindThatBook.Infrastructure.OpenLibrary.CatalogCacheCoordinator(),
            NullLogger<CachingBookCatalogSource>.Instance);

    [Fact]
    public async Task SearchAsync_returns_cached_value_on_second_call()
    {
        var inner = new Mock<IBookCatalogSource>();
        var books = new[] { BookFactory.Create() };
        inner.Setup(x => x.SearchAsync(It.IsAny<ExtractedBookInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);

        var sut = CreateSut(inner.Object);
        var hypothesis = new ExtractedBookInfo("hobbit", "tolkien", 1937, Array.Empty<string>());

        await sut.SearchAsync(hypothesis);
        await sut.SearchAsync(hypothesis);
        await sut.SearchAsync(hypothesis);

        inner.Verify(x => x.SearchAsync(It.IsAny<ExtractedBookInfo>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_skips_cache_for_empty_hypothesis()
    {
        var inner = new Mock<IBookCatalogSource>(MockBehavior.Strict);
        var sut = CreateSut(inner.Object);

        var result = await sut.SearchAsync(ExtractedBookInfo.Empty);

        result.Should().BeEmpty();
        inner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SearchAsync_does_not_cache_empty_results()
    {
        var inner = new Mock<IBookCatalogSource>();
        inner.Setup(x => x.SearchAsync(It.IsAny<ExtractedBookInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Book>());

        var sut = CreateSut(inner.Object);
        var hypothesis = new ExtractedBookInfo("nonexistent", null, null, Array.Empty<string>());

        await sut.SearchAsync(hypothesis);
        await sut.SearchAsync(hypothesis);

        inner.Verify(x => x.SearchAsync(It.IsAny<ExtractedBookInfo>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SearchAsync_treats_diacritic_variants_as_the_same_cache_key()
    {
        var inner = new Mock<IBookCatalogSource>();
        var books = new[] { BookFactory.Create() };
        inner.Setup(x => x.SearchAsync(It.IsAny<ExtractedBookInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);

        var sut = CreateSut(inner.Object);

        var withDiacritics = new ExtractedBookInfo("Cien años de soledad", "García Márquez", null, Array.Empty<string>());
        var asciiOnly = new ExtractedBookInfo("Cien anos de soledad", "Garcia Marquez", null, Array.Empty<string>());

        await sut.SearchAsync(withDiacritics);
        await sut.SearchAsync(asciiOnly);

        // The second call must hit the cache because the normalizer folds diacritics.
        inner.Verify(x => x.SearchAsync(It.IsAny<ExtractedBookInfo>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
