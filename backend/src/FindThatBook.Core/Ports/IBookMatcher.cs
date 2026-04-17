using FindThatBook.Core.Domain;

namespace FindThatBook.Core.Ports;

public interface IBookMatcher
{
    IReadOnlyList<BookCandidate> Rank(
        IReadOnlyList<Book> books,
        ExtractedBookInfo hypothesis,
        int maxResults);
}
