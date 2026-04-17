using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Matching.Rules;

public sealed class AuthorOnlyFallbackRule : IMatchRule
{
    private readonly ITextNormalizer _normalizer;

    public AuthorOnlyFallbackRule(ITextNormalizer normalizer) => _normalizer = normalizer;

    public MatchTier Tier => MatchTier.Weak;
    public string Name => nameof(AuthorOnlyFallbackRule);

    public BookCandidate? Evaluate(Book book, ExtractedBookInfo hypothesis)
    {
        if (!hypothesis.HasAuthor)
        {
            return null;
        }

        if (!RuleHelpers.AnyAuthorMatches(_normalizer, hypothesis.Author, book.PrimaryAuthors))
        {
            return null;
        }

        var explanation =
            $"Author-only fallback: '{hypothesis.Author}' matches primary author; title was not provided or did not match{RuleHelpers.YearSuffix(book, hypothesis)}.";

        return new BookCandidate(book, Tier, Name, explanation);
    }
}
