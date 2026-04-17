using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Matching;

/// <summary>
/// Default enricher used when enrichment is disabled (or in tests that want
/// to skip the extra HTTP round trips). Returns the input unchanged.
/// </summary>
public sealed class NoOpBookEnricher : IBookEnricher
{
    public Task<IReadOnlyList<Book>> EnrichAsync(IReadOnlyList<Book> books, CancellationToken cancellationToken = default)
        => Task.FromResult(books);
}
