using FindThatBook.Core.Domain;

namespace FindThatBook.Core.Ports;

public interface IBookCatalogSource
{
    Task<IReadOnlyList<Book>> SearchAsync(ExtractedBookInfo hypothesis, CancellationToken cancellationToken = default);
}
