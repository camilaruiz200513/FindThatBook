using FindThatBook.Core.Domain;
using FindThatBook.Core.Matching;
using FindThatBook.Core.Matching.Rules;
using FindThatBook.Core.Models;
using FindThatBook.Core.Ports;
using FindThatBook.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests.Core.Matching;

public class BookMatcherTests
{
    private readonly TextNormalizer _normalizer = new();
    private readonly IOptions<MatchingOptions> _options = Options.Create(new MatchingOptions());

    private BookMatcher CreateMatcher() => new(
        new IMatchRule[]
        {
            new ExactTitlePrimaryAuthorRule(_normalizer),
            new ExactTitleContributorAuthorRule(_normalizer),
            new NearTitleAuthorRule(_normalizer, _options),
            new AuthorOnlyFallbackRule(_normalizer),
            new WeakMatchRule(_normalizer, _options),
        },
        _normalizer);

    [Fact]
    public void Rank_selects_exact_tier_over_weaker_matches_for_same_book()
    {
        var matcher = CreateMatcher();
        var book = BookFactory.Create(title: "The Hobbit", primary: new[] { "J.R.R. Tolkien" });
        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", null, Array.Empty<string>());

        var result = matcher.Rank(new[] { book }, hypothesis, 5);

        result.Should().ContainSingle();
        result[0].Tier.Should().Be(MatchTier.Exact);
        result[0].RuleName.Should().Be(nameof(ExactTitlePrimaryAuthorRule));
    }

    [Fact]
    public void Rank_orders_candidates_by_tier_descending_then_year_bonus()
    {
        var matcher = CreateMatcher();
        var exactMatchWrongYear = BookFactory.Create(title: "The Hobbit", primary: new[] { "Tolkien" }, year: 1977);
        var strongContrib = BookFactory.Create(
            title: "The Hobbit",
            primary: new[] { "Peter Jackson" },
            contributors: new[] { "Tolkien" },
            year: 1937);

        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", 1937, Array.Empty<string>());

        var result = matcher.Rank(new[] { strongContrib, exactMatchWrongYear }, hypothesis, 5);

        result.Should().HaveCount(2);
        result[0].Tier.Should().Be(MatchTier.Exact);
        result[1].Tier.Should().Be(MatchTier.Strong);
    }

    [Fact]
    public void Rank_respects_maxResults_limit()
    {
        var matcher = CreateMatcher();
        // Distinct works so dedup can't merge them — use unique titles/years.
        var books = Enumerable.Range(0, 10)
            .Select(i => BookFactory.Create(
                title: $"Unique Title {i}",
                primary: new[] { "Tolkien" },
                workId: $"/works/OL{i}",
                year: 1900 + i))
            .ToArray();
        var hypothesis = new ExtractedBookInfo(null, "Tolkien", null, Array.Empty<string>());

        matcher.Rank(books, hypothesis, 3).Should().HaveCount(3);
    }

    [Fact]
    public void Rank_returns_empty_for_no_matches()
    {
        var matcher = CreateMatcher();
        var book = BookFactory.Create(title: "Moby Dick", primary: new[] { "Melville" });
        var hypothesis = new ExtractedBookInfo("harry potter", "rowling", null, Array.Empty<string>());

        matcher.Rank(new[] { book }, hypothesis, 5).Should().BeEmpty();
    }

    [Fact]
    public void Rank_returns_empty_when_maxResults_is_zero()
    {
        var matcher = CreateMatcher();
        var book = BookFactory.Create();
        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", null, Array.Empty<string>());

        matcher.Rank(new[] { book }, hypothesis, 0).Should().BeEmpty();
    }

    [Fact]
    public void Rank_deduplicates_candidates_with_same_title_and_year_keeping_highest_tier()
    {
        var matcher = CreateMatcher();
        // Same work returned twice by Open Library with different workIds —
        // can happen when /search.json folds editions with different cover_edition_keys.
        var canonical = BookFactory.Create(
            title: "The Hobbit",
            workId: "/works/OL1W",
            primary: new[] { "J.R.R. Tolkien" },
            year: 1937);
        var dup = BookFactory.Create(
            title: "The Hobbit, or There and Back Again",
            workId: "/works/OL2W",
            primary: new[] { "J.R.R. Tolkien" },
            year: 1937);

        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", 1937, Array.Empty<string>());

        var result = matcher.Rank(new[] { canonical, dup }, hypothesis, 5);

        result.Should().ContainSingle("both map to the same (normalized title, year)");
        result[0].Tier.Should().Be(MatchTier.Exact);
    }
}
