using FindThatBook.Core.Domain;

namespace FindThatBook.Core.Ports;

/// <summary>
/// Resolves the canonical works list for a specific author (backed by
/// <c>/authors/{key}/works.json</c>). Used for the author-only fallback tier
/// to supplement the keyword-based <c>/search.json?author=…</c> pool with the
/// author's actual bibliography.
/// </summary>
public interface IAuthorWorksSource
{
    /// <summary>
    /// Returns up to <paramref name="limit"/> works by the given author, or
    /// an empty list if the author cannot be resolved.
    /// </summary>
    Task<IReadOnlyList<Book>> FetchByAuthorNameAsync(string authorName, int limit, CancellationToken cancellationToken = default);
}
