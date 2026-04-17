using System.Text.Json.Serialization;

namespace FindThatBook.Core.Domain;

public sealed record ExtractedBookInfo(
    string? Title,
    string? Author,
    int? Year,
    IReadOnlyList<string> Keywords)
{
    [JsonIgnore]
    public static ExtractedBookInfo Empty { get; } =
        new(null, null, null, Array.Empty<string>());

    [JsonIgnore] public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    [JsonIgnore] public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
    [JsonIgnore] public bool HasKeywords => Keywords.Count > 0;
    [JsonIgnore] public bool IsEmpty => !HasTitle && !HasAuthor && !HasKeywords && Year is null;
}
