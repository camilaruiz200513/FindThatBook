using FindThatBook.Core.Domain;

namespace FindThatBook.Core.Ports;

/// <summary>
/// Second pass over the shortlist of candidates to pull authoritative metadata
/// that <c>/search.json</c> alone doesn't expose — most importantly, the real
/// primary vs contributor split (from <c>/works/{id}.json</c> + <c>/authors/{key}.json</c>).
/// Keeping this as a separate port lets the happy path work with the cheap
/// search, and a NoOp implementation disable the extra round trips for tests
/// or environments that don't want the latency.
/// </summary>
public interface IBookEnricher
{
    Task<IReadOnlyList<Book>> EnrichAsync(IReadOnlyList<Book> books, CancellationToken cancellationToken = default);
}
