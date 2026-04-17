using FindThatBook.Core.Domain;
using FindThatBook.Core.Matching;
using FindThatBook.Core.Matching.Rules;
using FindThatBook.Core.Models;
using FindThatBook.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Tests.Core.Matching.Rules;

public class MatchRuleTests
{
    private readonly TextNormalizer _normalizer = new();
    private readonly IOptions<MatchingOptions> _options = Options.Create(new MatchingOptions());

    [Fact]
    public void ExactTitlePrimaryAuthor_returns_exact_tier_when_both_match()
    {
        var rule = new ExactTitlePrimaryAuthorRule(_normalizer);
        var book = BookFactory.Create(title: "The Hobbit", primary: new[] { "J.R.R. Tolkien" });
        var hypothesis = new ExtractedBookInfo("the hobbit", "tolkien", 1937, Array.Empty<string>());

        var result = rule.Evaluate(book, hypothesis);

        result.Should().NotBeNull();
        result!.Tier.Should().Be(MatchTier.Exact);
        result.Explanation.Should().Contain("primary author");
    }

    [Fact]
    public void ExactTitlePrimaryAuthor_matches_when_book_title_has_subtitle_but_query_does_not()
    {
        // Challenge-cited scenario: user types "The Hobbit", Open Library returns
        // the work with full subtitle "The Hobbit, or There and Back Again".
        var rule = new ExactTitlePrimaryAuthorRule(_normalizer);
        var book = BookFactory.Create(
            title: "The Hobbit, or There and Back Again",
            primary: new[] { "J.R.R. Tolkien" });
        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", null, Array.Empty<string>());

        var result = rule.Evaluate(book, hypothesis);

        result.Should().NotBeNull();
        result!.Tier.Should().Be(MatchTier.Exact);
    }

    [Fact]
    public void ExactTitlePrimaryAuthor_returns_null_when_author_is_only_a_contributor()
    {
        var rule = new ExactTitlePrimaryAuthorRule(_normalizer);
        var book = BookFactory.Create(
            title: "The Hobbit",
            primary: new[] { "Peter Jackson" },
            contributors: new[] { "J.R.R. Tolkien" });
        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", null, Array.Empty<string>());

        rule.Evaluate(book, hypothesis).Should().BeNull();
    }

    [Fact]
    public void ExactTitleContributorAuthor_returns_strong_tier_and_mentions_contributor()
    {
        var rule = new ExactTitleContributorAuthorRule(_normalizer);
        var book = BookFactory.Create(
            title: "The Hobbit",
            primary: new[] { "Peter Jackson" },
            contributors: new[] { "J.R.R. Tolkien" });
        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", null, Array.Empty<string>());

        var result = rule.Evaluate(book, hypothesis);

        result.Should().NotBeNull();
        result!.Tier.Should().Be(MatchTier.Strong);
        result.Explanation.Should().Contain("contributor");
    }

    [Fact]
    public void ExactTitleContributorAuthor_returns_null_when_author_is_primary()
    {
        var rule = new ExactTitleContributorAuthorRule(_normalizer);
        var book = BookFactory.Create(title: "The Hobbit", primary: new[] { "J.R.R. Tolkien" });
        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", null, Array.Empty<string>());

        rule.Evaluate(book, hypothesis).Should().BeNull();
    }

    [Fact]
    public void NearTitleAuthor_matches_on_token_similarity_above_threshold()
    {
        var rule = new NearTitleAuthorRule(_normalizer, _options);
        var book = BookFactory.Create(title: "Harry Potter and the Philosopher's Stone",
            primary: new[] { "J.K. Rowling" });
        var hypothesis = new ExtractedBookInfo("harry potter philosopher stone", "Rowling", null, Array.Empty<string>());

        var result = rule.Evaluate(book, hypothesis);

        result.Should().NotBeNull();
        result!.Tier.Should().Be(MatchTier.Good);
    }

    [Fact]
    public void NearTitleAuthor_returns_null_when_similarity_is_below_threshold()
    {
        var rule = new NearTitleAuthorRule(_normalizer, _options);
        var book = BookFactory.Create(title: "Completely Different Book", primary: new[] { "J.K. Rowling" });
        var hypothesis = new ExtractedBookInfo("harry potter", "Rowling", null, Array.Empty<string>());

        rule.Evaluate(book, hypothesis).Should().BeNull();
    }

    [Fact]
    public void AuthorOnlyFallback_matches_author_without_title()
    {
        var rule = new AuthorOnlyFallbackRule(_normalizer);
        var book = BookFactory.Create(title: "Any Book", primary: new[] { "George Orwell" });
        var hypothesis = new ExtractedBookInfo(null, "Orwell", null, Array.Empty<string>());

        var result = rule.Evaluate(book, hypothesis);

        result.Should().NotBeNull();
        result!.Tier.Should().Be(MatchTier.Weak);
        result.Explanation.Should().Contain("Author-only");
    }

    [Fact]
    public void WeakMatch_matches_on_keyword_hit_in_subjects()
    {
        var rule = new WeakMatchRule(_normalizer, _options);
        var book = BookFactory.Create(
            title: "Some Book",
            subjects: new[] { "Fantasy", "Illustrated edition" });
        var hypothesis = new ExtractedBookInfo(null, null, null, new[] { "illustrated" });

        var result = rule.Evaluate(book, hypothesis);

        result.Should().NotBeNull();
        result!.Tier.Should().Be(MatchTier.Weak);
    }

    [Fact]
    public void WeakMatch_returns_null_when_nothing_overlaps()
    {
        var rule = new WeakMatchRule(_normalizer, _options);
        var book = BookFactory.Create(title: "Moby Dick", subjects: new[] { "whale" });
        var hypothesis = new ExtractedBookInfo("harry potter", null, null, new[] { "wizards" });

        rule.Evaluate(book, hypothesis).Should().BeNull();
    }
}
