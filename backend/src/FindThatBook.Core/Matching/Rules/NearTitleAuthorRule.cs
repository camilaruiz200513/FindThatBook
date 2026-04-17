using FindThatBook.Core.Domain;
using FindThatBook.Core.Models;
using FindThatBook.Core.Ports;
using Microsoft.Extensions.Options;

namespace FindThatBook.Core.Matching.Rules;

public sealed class NearTitleAuthorRule : IMatchRule
{
    private readonly ITextNormalizer _normalizer;
    private readonly MatchingOptions _options;

    public NearTitleAuthorRule(ITextNormalizer normalizer, IOptions<MatchingOptions> options)
    {
        _normalizer = normalizer;
        _options = options.Value;
    }

    public MatchTier Tier => MatchTier.Good;
    public string Name => nameof(NearTitleAuthorRule);

    public BookCandidate? Evaluate(Book book, ExtractedBookInfo hypothesis)
    {
        if (!hypothesis.HasTitle || !hypothesis.HasAuthor)
        {
            return null;
        }

        var similarity = _normalizer.Similarity(book.Title, hypothesis.Title);
        if (similarity < _options.NearTitleThreshold)
        {
            return null;
        }

        if (!RuleHelpers.AnyAuthorMatches(_normalizer, hypothesis.Author, book.AllAuthors))
        {
            return null;
        }

        var similarityPct = (int)Math.Round(similarity * 100);
        var explanation =
            $"Near title match ({similarityPct}% token overlap) with author match{RuleHelpers.YearSuffix(book, hypothesis)}.";

        return new BookCandidate(book, Tier, Name, explanation);
    }
}
