using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Matching.Rules;

internal static class RuleHelpers
{
    public static bool TitlesMatchExactly(ITextNormalizer normalizer, string? a, string? b)
    {
        var na = normalizer.Normalize(a);
        var nb = normalizer.Normalize(b);
        if (na.Length > 0 && nb.Length > 0 && na == nb)
        {
            return true;
        }

        // Subtitle-aware: "The Hobbit" should match "The Hobbit, or There and Back Again".
        var sa = normalizer.Normalize(normalizer.StripSubtitle(a));
        var sb = normalizer.Normalize(normalizer.StripSubtitle(b));
        return sa.Length > 0 && sb.Length > 0 && sa == sb;
    }

    public static bool AuthorMatches(ITextNormalizer normalizer, string? queryAuthor, string bookAuthor)
    {
        var normalizedQuery = normalizer.Normalize(queryAuthor);
        var normalizedBook = normalizer.Normalize(bookAuthor);

        if (normalizedQuery.Length == 0 || normalizedBook.Length == 0)
        {
            return false;
        }

        if (normalizedQuery == normalizedBook)
        {
            return true;
        }

        // Last-name / partial match (e.g. "tolkien" ↔ "j r r tolkien")
        var queryTokens = normalizer.Tokenize(queryAuthor);
        var bookTokens = normalizer.Tokenize(bookAuthor);

        return queryTokens.All(q => bookTokens.Any(b => b == q));
    }

    public static bool AnyAuthorMatches(ITextNormalizer normalizer, string? queryAuthor, IEnumerable<string> authors)
        => authors.Any(a => AuthorMatches(normalizer, queryAuthor, a));

    public static string YearSuffix(Book book, ExtractedBookInfo hypothesis)
    {
        if (hypothesis.Year is null || book.FirstPublishYear is null)
        {
            return string.Empty;
        }

        return book.FirstPublishYear == hypothesis.Year
            ? $"; year {book.FirstPublishYear} matches"
            : $"; year mismatch (expected {hypothesis.Year}, got {book.FirstPublishYear})";
    }
}
