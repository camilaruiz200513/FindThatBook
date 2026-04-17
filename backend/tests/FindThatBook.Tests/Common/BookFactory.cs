using FindThatBook.Core.Domain;

namespace FindThatBook.Tests.Common;

public static class BookFactory
{
    public static Book Create(
        string title = "The Hobbit",
        string workId = "/works/OL262758W",
        IReadOnlyList<string>? primary = null,
        IReadOnlyList<string>? contributors = null,
        int? year = 1937,
        string? coverId = "12345",
        IReadOnlyList<string>? subjects = null,
        IReadOnlyList<string>? isbns = null) =>
        new(
            workId,
            title,
            primary ?? new[] { "J.R.R. Tolkien" },
            contributors ?? Array.Empty<string>(),
            year,
            coverId,
            subjects ?? Array.Empty<string>(),
            isbns ?? Array.Empty<string>());
}
