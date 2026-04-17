using System.Text.Json.Serialization;

namespace FindThatBook.Core.Domain;

public sealed record Book(
    string WorkId,
    string Title,
    IReadOnlyList<string> PrimaryAuthors,
    IReadOnlyList<string> Contributors,
    int? FirstPublishYear,
    string? CoverId,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<string> Isbns)
{
    public string OpenLibraryUrl => $"https://openlibrary.org{WorkId}";

    public string? CoverUrl => string.IsNullOrWhiteSpace(CoverId)
        ? null
        : $"https://covers.openlibrary.org/b/id/{CoverId}-M.jpg";

    [JsonIgnore]
    public IEnumerable<string> AllAuthors =>
        PrimaryAuthors.Concat(Contributors).Distinct(StringComparer.OrdinalIgnoreCase);
}
