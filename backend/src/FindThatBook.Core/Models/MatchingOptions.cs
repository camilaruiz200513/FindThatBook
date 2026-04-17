namespace FindThatBook.Core.Models;

public sealed class MatchingOptions
{
    public const string SectionName = "Matching";

    public double NearTitleThreshold { get; set; } = 0.7;
    public double WeakMatchThreshold { get; set; } = 0.35;
    public int DefaultMaxResults { get; set; } = 5;

    // When enabled, the top-K ranked candidates are passed to the LLM for a
    // second opinion on ordering. The deterministic tier-based rank still
    // drives fallback behavior; the LLM only gets the final shortlist.
    public bool UseLlmRerank { get; set; } = true;
    public int RerankTopK { get; set; } = 5;

    // When enabled, author-only hypotheses (no title) are augmented with the
    // canonical works list from /authors/{key}/works.json.
    public int AuthorWorksFallbackLimit { get; set; } = 10;
}
