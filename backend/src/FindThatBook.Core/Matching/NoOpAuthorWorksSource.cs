using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Matching;

public sealed class NoOpAuthorWorksSource : IAuthorWorksSource
{
    public Task<IReadOnlyList<Book>> FetchByAuthorNameAsync(string authorName, int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Book>>(Array.Empty<Book>());
}
