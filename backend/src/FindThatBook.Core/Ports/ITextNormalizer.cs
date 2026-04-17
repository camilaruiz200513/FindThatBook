namespace FindThatBook.Core.Ports;

public interface ITextNormalizer
{
    string Normalize(string? input);
    IReadOnlyList<string> Tokenize(string? input);
    double Similarity(string? a, string? b);

    /// <summary>
    /// Truncates a title at common subtitle markers (":", "(", ", or ") so that
    /// "The Hobbit, or There and Back Again" matches "The Hobbit".
    /// </summary>
    string StripSubtitle(string? input);
}
