using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Matching;

public sealed class BookMatcher : IBookMatcher
{
    private readonly IReadOnlyList<IMatchRule> _rules;
    private readonly ITextNormalizer _normalizer;

    public BookMatcher(IEnumerable<IMatchRule> rules, ITextNormalizer normalizer)
    {
        _rules = rules.OrderByDescending(r => r.Tier).ToArray();
        _normalizer = normalizer;
    }

    public IReadOnlyList<BookCandidate> Rank(
        IReadOnlyList<Book> books,
        ExtractedBookInfo hypothesis,
        int maxResults)
    {
        if (books.Count == 0 || maxResults <= 0)
        {
            return Array.Empty<BookCandidate>();
        }

        var matches = new List<BookCandidate>(books.Count);

        foreach (var book in books)
        {
            foreach (var rule in _rules)
            {
                var candidate = rule.Evaluate(book, hypothesis);
                if (candidate is not null)
                {
                    matches.Add(candidate);
                    break;
                }
            }
        }

        return Deduplicate(matches)
            .OrderByDescending(c => c.Tier)
            .ThenByDescending(c => YearBonus(c, hypothesis))
            .Take(maxResults)
            .ToArray();
    }

    /// <summary>
    /// Open Library occasionally surfaces the same work twice (different
    /// <c>cover_edition_key</c> values rolled into separate docs). Fold them
    /// on (normalized stripped title, first publish year), keeping the
    /// highest-tier candidate.
    /// </summary>
    private IEnumerable<BookCandidate> Deduplicate(IEnumerable<BookCandidate> candidates)
    {
        return candidates
            .GroupBy(c => new
            {
                Title = _normalizer.Normalize(_normalizer.StripSubtitle(c.Book.Title)),
                Year = c.Book.FirstPublishYear,
            })
            .Select(g => g.OrderByDescending(c => c.Tier).First());
    }

    private static int YearBonus(BookCandidate candidate, ExtractedBookInfo hypothesis)
    {
        if (hypothesis.Year is null || candidate.Book.FirstPublishYear is null)
        {
            return 0;
        }

        return candidate.Book.FirstPublishYear == hypothesis.Year ? 1 : 0;
    }
}
