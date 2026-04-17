using System.Globalization;
using System.Text;
using FindThatBook.Core.Ports;

namespace FindThatBook.Core.Matching;

public sealed class TextNormalizer : ITextNormalizer
{
    // Union of common English and Spanish stopwords. Kept intentionally small —
    // "book", "libro" removed because they appear in genuine titles. The set is
    // lowercase ordinal because Normalize() lowercases first.
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        // English
        "the", "a", "an", "of", "and", "or", "to", "in", "on", "at", "by", "for",
        "with", "about", "from", "as", "is", "are", "was", "were", "be", "book",
        // Spanish
        "el", "la", "los", "las", "un", "una", "unos", "unas",
        "de", "del", "y", "o", "a", "en", "con", "por", "para",
        "que", "es", "ser", "al", "lo", "libro",
    };

    public string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var decomposed = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '/')
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }
            }
        }

        return sb.ToString().Trim();
    }

    public IReadOnlyList<string> Tokenize(string? input)
    {
        var normalized = Normalize(input);
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !Stopwords.Contains(t))
            .Select(Stem)
            .ToArray();
    }

    private static string Stem(string token)
    {
        // Naive plural stem: strip a single trailing 's' but not 'ss' endings.
        // Good enough for book titles (covers both English plurals and Spanish
        // plurals like "anillos" → "anillo") without pulling in a full stemmer.
        if (token.Length > 3 && token[^1] == 's' && token[^2] != 's')
        {
            return token[..^1];
        }
        return token;
    }

    public double Similarity(string? a, string? b)
    {
        var direct = JaccardSimilarity(a, b);
        var strippedA = StripSubtitle(a);
        var strippedB = StripSubtitle(b);

        // If either side shrank, the subtitle-less variant might match better
        // (e.g. "The Hobbit" vs "The Hobbit, or There and Back Again").
        if (strippedA == a && strippedB == b)
        {
            return direct;
        }

        var stripped = JaccardSimilarity(strippedA, strippedB);
        return Math.Max(direct, stripped);
    }

    public string StripSubtitle(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var cutAt = input.Length;
        ReadOnlySpan<string> markers = new[] { ":", "(", ";", ", or " };
        foreach (var marker in markers)
        {
            var idx = input.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx > 0 && idx < cutAt)
            {
                cutAt = idx;
            }
        }
        return input[..cutAt].TrimEnd();
    }

    private double JaccardSimilarity(string? a, string? b)
    {
        var tokensA = Tokenize(a);
        var tokensB = Tokenize(b);

        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0d;
        }

        var setA = new HashSet<string>(tokensA, StringComparer.Ordinal);
        var setB = new HashSet<string>(tokensB, StringComparer.Ordinal);

        var intersection = setA.Intersect(setB, StringComparer.Ordinal).Count();
        var union = setA.Count + setB.Count - intersection;

        return union == 0 ? 0d : (double)intersection / union;
    }
}
