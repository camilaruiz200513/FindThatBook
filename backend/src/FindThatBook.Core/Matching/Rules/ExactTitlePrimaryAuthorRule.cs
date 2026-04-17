using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Matching.Rules;

public sealed class ExactTitlePrimaryAuthorRule : IMatchRule
{
    private readonly ITextNormalizer _normalizer;

    public ExactTitlePrimaryAuthorRule(ITextNormalizer normalizer) => _normalizer = normalizer;

    public MatchTier Tier => MatchTier.Exact;
    public string Name => nameof(ExactTitlePrimaryAuthorRule);

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

        if (!RuleHelpers.AnyAuthorMatches(_normalizer, hypothesis.Author, book.PrimaryAuthors))
        {
            return null;
        }

        var explanation =
            $"Exact title match and primary author match ('{hypothesis.Author}'){RuleHelpers.YearSuffix(book, hypothesis)}.";

        return new BookCandidate(book, Tier, Name, explanation);
    }
}
