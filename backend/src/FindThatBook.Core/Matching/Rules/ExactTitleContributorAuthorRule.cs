using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Matching.Rules;

public sealed class ExactTitleContributorAuthorRule : IMatchRule
{
    private readonly ITextNormalizer _normalizer;

    public ExactTitleContributorAuthorRule(ITextNormalizer normalizer) => _normalizer = normalizer;

    public MatchTier Tier => MatchTier.Strong;
    public string Name => nameof(ExactTitleContributorAuthorRule);

    public BookCandidate? Evaluate(Book book, ExtractedBookInfo hypothesis)
    {
        if (!hypothesis.HasTitle || !hypothesis.HasAuthor)
        {
            return null;
        }

        if (!RuleHelpers.TitlesMatchExactly(_normalizer, book.Title, hypothesis.Title))
        {
            return null;
        }

        // Must NOT be primary — that's covered by the higher-tier rule.
        if (RuleHelpers.AnyAuthorMatches(_normalizer, hypothesis.Author, book.PrimaryAuthors))
        {
            return null;
        }

        if (!RuleHelpers.AnyAuthorMatches(_normalizer, hypothesis.Author, book.Contributors))
        {
            return null;
        }

        var explanation =
            $"Exact title match; '{hypothesis.Author}' appears as contributor (not primary author){RuleHelpers.YearSuffix(book, hypothesis)}.";

        return new BookCandidate(book, Tier, Name, explanation);
    }
}
