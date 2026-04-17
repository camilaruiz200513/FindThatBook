using FindThatBook.Core.Domain;

namespace FindThatBook.Core.Models;

public sealed record FindBookResponse(
    string OriginalQuery,
    ExtractedBookInfo Hypothesis,
    IReadOnlyList<BookCandidate> Candidates,
    int TotalCandidates,
    TimeSpan ProcessingTime);
