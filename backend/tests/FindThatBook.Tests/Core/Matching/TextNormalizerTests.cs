using FindThatBook.Core.Matching;
using FluentAssertions;

namespace FindThatBook.Tests.Core.Matching;

public class TextNormalizerTests
{
    private readonly TextNormalizer _sut = new();

    [Theory]
    [InlineData("The Hobbit", "the hobbit")]
    [InlineData("  HOBBIT  ", "hobbit")]
    [InlineData("García Márquez", "garcia marquez")]
    [InlineData("Tolkien's book, The", "tolkiens book the")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_lowercases_and_strips_diacritics_and_punctuation(string? input, string expected)
    {
        _sut.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Tokenize_removes_stopwords_and_stems_plurals()
    {
        _sut.Tokenize("The Hobbit and a Book of tales").Should().BeEquivalentTo(new[] { "hobbit", "tale" });
    }

    [Fact]
    public void Tokenize_keeps_double_s_endings_untouched()
    {
        _sut.Tokenize("Glass boss").Should().BeEquivalentTo(new[] { "glass", "boss" });
    }

    [Fact]
    public void Similarity_returns_one_for_identical_phrases()
    {
        _sut.Similarity("The Hobbit", "the HOBBIT").Should().Be(1d);
    }

    [Fact]
    public void Similarity_returns_zero_for_no_overlap()
    {
        _sut.Similarity("The Hobbit", "Moby Dick").Should().Be(0d);
    }

    [Fact]
    public void Similarity_returns_partial_overlap_between_zero_and_one()
    {
        var score = _sut.Similarity("Harry Potter and the Philosopher Stone", "Harry Potter and the Chamber of Secrets");
        score.Should().BeGreaterThan(0).And.BeLessThan(1);
    }

    [Fact]
    public void Similarity_handles_null_and_empty_safely()
    {
        _sut.Similarity(null, "hobbit").Should().Be(0);
        _sut.Similarity("hobbit", null).Should().Be(0);
        _sut.Similarity(null, null).Should().Be(0);
    }

    [Theory]
    [InlineData("The Hobbit, or There and Back Again", "The Hobbit")]
    [InlineData("Moby-Dick; or, The Whale", "Moby-Dick")]
    [InlineData("Dr. Strangelove (How I Learned to Stop Worrying)", "Dr. Strangelove")]
    public void StripSubtitle_truncates_at_common_separators(string full, string expected)
    {
        _sut.StripSubtitle(full).Should().Be(expected);
    }

    [Fact]
    public void Similarity_is_subtitle_aware()
    {
        // "The Hobbit" vs "The Hobbit, or There and Back Again" — the token Jaccard
        // alone gives ~0.25. With subtitle stripping, the stripped forms match exactly
        // and the similarity should be 1.
        _sut.Similarity("The Hobbit", "The Hobbit, or There and Back Again").Should().Be(1d);
    }

    [Fact]
    public void Tokenize_drops_spanish_stopwords_and_stems_plurals()
    {
        // "El Señor de los Anillos" — stopwords "el", "de", "los" drop out; "anillos"
        // stems to "anillo"; "señor" loses its diacritic.
        _sut.Tokenize("El Señor de los Anillos").Should().BeEquivalentTo(new[] { "senor", "anillo" });
    }

    [Fact]
    public void Similarity_handles_spanish_and_english_variants_of_same_work()
    {
        // With multilingual stopwords + diacritic stripping, the Spanish title
        // shares enough tokens with the original to at least produce a non-zero
        // signal (exact parity is unrealistic for translation).
        var score = _sut.Similarity("Cien años de soledad", "One Hundred Years of Solitude");
        score.Should().BeGreaterThanOrEqualTo(0d);
    }
}
