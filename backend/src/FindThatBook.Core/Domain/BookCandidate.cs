namespace FindThatBook.Core.Domain;

public sealed record BookCandidate(
    Book Book,
    MatchTier Tier,
    string RuleName,
    string Explanation);
