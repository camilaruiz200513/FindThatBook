using FindThatBook.Core.Domain;

namespace FindThatBook.Core.Ports;

public interface ILlmService
{
    /// <summary>
    /// Turn a noisy user query into a structured hypothesis
    /// (<see cref="ExtractedBookInfo"/>). Returns <c>Empty</c> on any failure
    /// so the orchestrator can decide what to do (e.g. fall back to a
    /// heuristic extractor).
    /// </summary>
    Task<ExtractedBookInfo> ExtractAsync(string dirtyQuery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Second opinion on ordering: given the query, the extracted hypothesis,
    /// and the shortlist produced by the deterministic matcher, return the
    /// same candidates reordered (or the original order if the LLM doesn't
    /// respond usefully). The implementation must not drop candidates; if it
    /// returns a partial ordering, the orchestrator is free to append the
    /// missing ones in their original position.
    /// </summary>
    Task<IReadOnlyList<string>> RerankAsync(
        string originalQuery,
        ExtractedBookInfo hypothesis,
        IReadOnlyList<BookCandidate> candidates,
        CancellationToken cancellationToken = default);
}
