using FindThatBook.Core.Domain;

namespace FindThatBook.Core.Ports;

public interface IMatchRule
{
    MatchTier Tier { get; }
    string Name { get; }
    BookCandidate? Evaluate(Book book, ExtractedBookInfo hypothesis);
}
