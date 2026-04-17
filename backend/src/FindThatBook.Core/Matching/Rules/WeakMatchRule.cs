using FindThatBook.Core.Domain;
using FindThatBook.Core.Models;
using FindThatBook.Core.Ports;
using Microsoft.Extensions.Options;

namespace FindThatBook.Core.Matching.Rules;

public sealed class WeakMatchRule : IMatchRule
{
    private readonly ITextNormalizer _normalizer;
    private readonly MatchingOptions _options;

    public WeakMatchRule(ITextNormalizer normalizer, IOptions<MatchingOptions> options)
    {
        _normalizer = normalizer;
        _options = options.Value;
    }

    public MatchTier Tier => MatchTier.Weak;
    public string Name => nameof(WeakMatchRule);

    public BookCandidate? Evaluate(Book book, ExtractedBookInfo hypothesis)
    {
        if (hypothesis.IsEmpty)
        {
            return null;
        }

        var titleSimilarity = hypothesis.HasTitle
            ? _normalizer.Similarity(book.Title, hypothesis.Title)
            : 0d;

        var keywordHits = 0;
        if (hypothesis.HasKeywords)
        {
            var haystack = string.Join(' ', book.Subjects.Append(book.Title));
            var normalizedHaystack = _normalizer.Normalize(haystack);
            foreach (var keyword in hypothesis.Keywords)
            {
                var normalizedKeyword = _normalizer.Normalize(keyword);
                if (normalizedKeyword.Length > 0 && normalizedHaystack.Contains(normalizedKeyword, StringComparison.Ordinal))
                {
                    keywordHits++;
                }
            }
        }

        if (titleSimilarity < _options.WeakMatchThreshold && keywordHits == 0)
        {
            return null;
        }

        var parts = new List<string>();
        if (titleSimilarity > 0)
        {
            parts.Add($"title overlap {(int)Math.Round(titleSimilarity * 100)}%");
        }
        if (keywordHits > 0)
        {
            parts.Add($"{keywordHits} keyword hit(s)");
        }

        var explanation = $"Weak match: {string.Join(", ", parts)}{RuleHelpers.YearSuffix(book, hypothesis)}.";
        return new BookCandidate(book, Tier, Name, explanation);
    }
}
