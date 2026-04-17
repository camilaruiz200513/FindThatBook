using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FindThatBook.Core.Domain;
using FindThatBook.Core.Models;
using FindThatBook.Core.Ports;
using FindThatBook.Tests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FindThatBook.Tests.Api;

/// <summary>
/// End-to-end tests backed by the four queries explicitly listed in the assessment statement.
/// The LLM and catalog are mocked so these tests run offline and deterministically, but the
/// hypothesis and candidate pool for each query reflect what the real services would return.
/// </summary>
public class ChallengeQueryIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly WebApplicationFactory<Program> _factory;

    public ChallengeQueryIntegrationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient BuildClient(ExtractedBookInfo hypothesis, IReadOnlyList<Book> books) =>
        _factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureServices(services =>
            {
                TestHostHelpers.ReplaceExternalPorts(services, hypothesis, books);
                services.Configure<MatchingOptions>(o => o.UseLlmRerank = false);
            });
        }).CreateClient();

    private static async Task<FindBookResponse> PostAsync(HttpClient client, string query, int max = 5)
    {
        var response = await client.PostAsJsonAsync("/api/books/find", new FindBookRequest(query, max));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<FindBookResponse>(JsonOptions);
        payload.Should().NotBeNull();
        return payload!;
    }

    [Fact]
    public async Task mark_huckleberry_resolves_to_Twain_primary_author_Exact_tier()
    {
        var hypothesis = new ExtractedBookInfo(
            "The Adventures of Huckleberry Finn",
            "Mark Twain",
            null,
            Array.Empty<string>());

        var books = new[]
        {
            BookFactory.Create(
                title: "The Adventures of Huckleberry Finn",
                workId: "/works/OL62631W",
                primary: new[] { "Mark Twain" },
                year: 1884),
            BookFactory.Create(
                title: "Adventures of Huckleberry Finn (Illustrated)",
                workId: "/works/OL99999W",
                primary: new[] { "Unknown Editor" },
                contributors: new[] { "Mark Twain" },
                year: 2015),
        };

        var result = await PostAsync(BuildClient(hypothesis, books), "mark huckleberry");

        result.Hypothesis.Author.Should().Be("Mark Twain");
        result.Candidates.Should().NotBeEmpty();
        result.Candidates[0].Tier.Should().Be(MatchTier.Exact);
        result.Candidates[0].Explanation.Should().Contain("primary author");
    }

    [Fact]
    public async Task twilight_meyer_resolves_to_Meyer_primary_author_and_ranks_first_book()
    {
        var hypothesis = new ExtractedBookInfo(
            "Twilight",
            "Stephenie Meyer",
            null,
            Array.Empty<string>());

        var books = new[]
        {
            BookFactory.Create(
                title: "Twilight",
                workId: "/works/OL5800080W",
                primary: new[] { "Stephenie Meyer" },
                year: 2005),
            BookFactory.Create(
                title: "New Moon",
                workId: "/works/OL5800081W",
                primary: new[] { "Stephenie Meyer" },
                year: 2006),
        };

        var result = await PostAsync(BuildClient(hypothesis, books), "twilight meyer");

        result.Candidates.Should().HaveCountGreaterThanOrEqualTo(1);
        result.Candidates[0].Book.Title.Should().Be("Twilight");
        result.Candidates[0].Tier.Should().Be(MatchTier.Exact);
    }

    [Fact]
    public async Task tolkien_hobbit_illustrated_deluxe_1937_matches_with_subtitle_and_year_bonus()
    {
        // Challenge scenario: user types a rich query; Open Library returns both the
        // canonical work (with its full subtitle) and a later illustrated re-issue.
        // The 1937 canonical work must rank first because of the year match.
        var hypothesis = new ExtractedBookInfo(
            "The Hobbit",
            "J.R.R. Tolkien",
            1937,
            new[] { "illustrated", "deluxe" });

        var books = new[]
        {
            BookFactory.Create(
                title: "The Hobbit, or There and Back Again",
                workId: "/works/OL262758W",
                primary: new[] { "J.R.R. Tolkien" },
                year: 1937,
                subjects: new[] { "Fantasy", "Adventure" }),
            BookFactory.Create(
                title: "The Hobbit (Illustrated Deluxe Edition)",
                workId: "/works/OL999999W",
                primary: new[] { "J.R.R. Tolkien" },
                contributors: new[] { "Alan Lee" },
                year: 2013,
                subjects: new[] { "Fantasy", "Illustrated deluxe edition" }),
        };

        var result = await PostAsync(BuildClient(hypothesis, books), "tolkien hobbit illustrated deluxe 1937");

        result.Candidates.Should().HaveCountGreaterThanOrEqualTo(1);
        // Subtitle-aware exact match lets the 1937 canonical entry match at Exact tier.
        result.Candidates[0].Tier.Should().Be(MatchTier.Exact);
        result.Candidates[0].Book.FirstPublishYear.Should().Be(1937);
        result.Candidates[0].Explanation.Should().Contain("year 1937 matches");
    }

    [Fact]
    public async Task austen_bennet_falls_back_to_AuthorOnly_when_bennet_is_a_character_not_the_author()
    {
        // "Bennet" is a character (Elizabeth Bennet) in Pride and Prejudice, not an
        // author. The LLM should hand us Austen as the author and no title. The
        // matcher then relies on AuthorOnlyFallback to surface Austen's works.
        var hypothesis = new ExtractedBookInfo(
            null,
            "Jane Austen",
            null,
            new[] { "bennet" });

        var books = new[]
        {
            BookFactory.Create(
                title: "Pride and Prejudice",
                workId: "/works/OL1394110W",
                primary: new[] { "Jane Austen" },
                year: 1813,
                subjects: new[] { "Elizabeth Bennet", "Regency", "Romance" }),
            BookFactory.Create(
                title: "Sense and Sensibility",
                workId: "/works/OL66554W",
                primary: new[] { "Jane Austen" },
                year: 1811),
        };

        var result = await PostAsync(BuildClient(hypothesis, books), "austen bennet");

        result.Hypothesis.Title.Should().BeNull();
        result.Hypothesis.Author.Should().Be("Jane Austen");
        result.Candidates.Should().NotBeEmpty();
        result.Candidates.Should().OnlyContain(c => c.Tier == MatchTier.Weak);
        result.Candidates.Should().Contain(c => c.Book.Title == "Pride and Prejudice");
    }
}
