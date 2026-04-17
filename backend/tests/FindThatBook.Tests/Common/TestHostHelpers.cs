using FindThatBook.Core.Domain;
using FindThatBook.Core.Matching;
using FindThatBook.Core.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace FindThatBook.Tests.Common;

/// <summary>
/// Shared test-host configuration: swap the outbound ports (LLM, catalog,
/// enricher, author-works) for deterministic test doubles so integration
/// tests never hit the network.
/// </summary>
internal static class TestHostHelpers
{
    public static void ReplaceExternalPorts(
        IServiceCollection services,
        ExtractedBookInfo hypothesis,
        IReadOnlyList<Book> books)
    {
        services.RemoveAll<ILlmService>();
        services.RemoveAll<IBookCatalogSource>();
        services.RemoveAll<IBookEnricher>();
        services.RemoveAll<IAuthorWorksSource>();

        var llm = new Mock<ILlmService>();
        llm.Setup(x => x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hypothesis);
        llm.Setup(x => x.RerankAsync(
                It.IsAny<string>(),
                It.IsAny<ExtractedBookInfo>(),
                It.IsAny<IReadOnlyList<BookCandidate>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, ExtractedBookInfo _, IReadOnlyList<BookCandidate> cs, CancellationToken _) =>
                cs.Select(c => c.Book.WorkId).ToArray());
        services.AddSingleton(llm.Object);

        var catalog = new Mock<IBookCatalogSource>();
        catalog.Setup(x => x.SearchAsync(It.IsAny<ExtractedBookInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(books);
        services.AddSingleton(catalog.Object);

        services.AddSingleton<IBookEnricher, NoOpBookEnricher>();
        services.AddSingleton<IAuthorWorksSource, NoOpAuthorWorksSource>();
    }
}
