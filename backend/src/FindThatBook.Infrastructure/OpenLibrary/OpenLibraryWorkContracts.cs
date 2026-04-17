using System.Text.Json.Serialization;

namespace FindThatBook.Infrastructure.OpenLibrary;

// /works/{id}.json response (subset we care about).
internal sealed class OpenLibraryWork
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("authors")]
    public List<OpenLibraryWorkAuthor>? Authors { get; set; }
}

internal sealed class OpenLibraryWorkAuthor
{
    [JsonPropertyName("author")]
    public OpenLibraryKeyRef? Author { get; set; }
}

internal sealed class OpenLibraryKeyRef
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }
}

// /authors/{key}.json response (subset we care about).
internal sealed class OpenLibraryAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("personal_name")]
    public string? PersonalName { get; set; }
}

// /authors/{key}/works.json response (subset we care about).
internal sealed class OpenLibraryAuthorWorks
{
    [JsonPropertyName("entries")]
    public List<OpenLibraryAuthorWorkEntry>? Entries { get; set; }
}

internal sealed class OpenLibraryAuthorWorkEntry
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("first_publish_date")]
    public string? FirstPublishDate { get; set; }

    [JsonPropertyName("covers")]
    public List<int>? Covers { get; set; }
}
