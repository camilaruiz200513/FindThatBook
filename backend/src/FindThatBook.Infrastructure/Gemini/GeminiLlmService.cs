using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FindThatBook.Core.Domain;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindThatBook.Infrastructure.Gemini;

public sealed partial class GeminiLlmService : ILlmService
{
    internal const string HttpClientName = "Gemini";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // OpenAPI 3.0-style schema (Gemini-compatible subset). Forcing schema-bound
    // output eliminates the "LLM replied in prose / wrapped in markdown fences"
    // class of bugs — the model can only emit JSON that fits this shape.
    private static readonly object ExtractionSchema = new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", nullable = true },
            author = new { type = "string", nullable = true },
            year = new { type = "integer", nullable = true },
            keywords = new { type = "array", items = new { type = "string" } },
        },
        required = new[] { "title", "author", "year", "keywords" },
    };

    private static readonly object RerankSchema = new
    {
        type = "object",
        properties = new
        {
            order = new { type = "array", items = new { type = "string" } },
        },
        required = new[] { "order" },
    };

    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiLlmService> _logger;

    public GeminiLlmService(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        ILogger<GeminiLlmService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExtractedBookInfo> ExtractAsync(string dirtyQuery, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Gemini API key is not configured; falling back to heuristic extraction.");
            return HeuristicExtract(dirtyQuery);
        }

        if (string.IsNullOrWhiteSpace(dirtyQuery))
        {
            return ExtractedBookInfo.Empty;
        }

        try
        {
            var request = BuildExtractionRequest(dirtyQuery);
            // API key travels in the x-goog-api-key header (configured on the HttpClient in DI),
            // never in the URL — URLs can leak into proxy/CDN/error-tracker logs.
            var url = $"{_options.BaseUrl.TrimEnd('/')}/models/{_options.Model}:generateContent";

            var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Gemini returned {Status} for query '{Query}': {Body}",
                    (int)response.StatusCode, dirtyQuery, body);
                return HeuristicExtract(dirtyQuery);
            }

            var parsed = await response.Content.ReadFromJsonAsync<GeminiResponse>(JsonOptions, cancellationToken);
            var text = parsed?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Gemini returned empty candidate text for query '{Query}'.", dirtyQuery);
                return HeuristicExtract(dirtyQuery);
            }

            var extracted = ParseExtraction(text);
            return extracted.IsEmpty ? HeuristicExtract(dirtyQuery) : extracted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini extraction failed for query '{Query}'; using heuristic fallback.", dirtyQuery);
            return HeuristicExtract(dirtyQuery);
        }
    }

    public async Task<IReadOnlyList<string>> RerankAsync(
        string originalQuery,
        ExtractedBookInfo hypothesis,
        IReadOnlyList<BookCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || candidates.Count < 2)
        {
            return candidates.Select(c => c.Book.WorkId).ToArray();
        }

        try
        {
            var request = BuildRerankRequest(originalQuery, hypothesis, candidates);
            var url = $"{_options.BaseUrl.TrimEnd('/')}/models/{_options.Model}:generateContent";

            var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini rerank failed with {Status}; keeping deterministic order.", (int)response.StatusCode);
                return candidates.Select(c => c.Book.WorkId).ToArray();
            }

            var parsed = await response.Content.ReadFromJsonAsync<GeminiResponse>(JsonOptions, cancellationToken);
            var text = parsed?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return candidates.Select(c => c.Book.WorkId).ToArray();
            }

            var order = ParseRerankOrder(text);
            return order.Count == 0
                ? candidates.Select(c => c.Book.WorkId).ToArray()
                : order;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini rerank failed; keeping deterministic order.");
            return candidates.Select(c => c.Book.WorkId).ToArray();
        }
    }

    private GeminiRequest BuildExtractionRequest(string dirtyQuery)
    {
        var prompt = $$"""
        You are a book identification assistant. Given a noisy user query, return a single JSON object
        describing the most likely book. Use null when the information is not derivable.

        Query: "{{dirtyQuery}}"

        Output strictly this JSON shape (no markdown, no commentary):
        {
          "title": "<canonical book title or null>",
          "author": "<full author name or null>",
          "year": <publication year as integer or null>,
          "keywords": ["extra","descriptors","such","as","illustrated","first edition"]
        }

        Rules:
        - Correct obvious misspellings and fragments (e.g. "mark huckleberry" -> title "The Adventures of Huckleberry Finn", author "Mark Twain").
        - Prefer full author names (e.g. "J.R.R. Tolkien" not "Tolkien").
        - Keep keywords short (edition, format, subject), max 5 items, empty array if none.
        - Do NOT invent years. Return null when uncertain.
        """;

        return new GeminiRequest
        {
            Contents = { new GeminiContent { Parts = { new GeminiPart { Text = prompt } } } },
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = _options.Temperature,
                MaxOutputTokens = _options.MaxOutputTokens,
                ResponseSchema = ExtractionSchema,
            },
        };
    }

    private GeminiRequest BuildRerankRequest(
        string originalQuery,
        ExtractedBookInfo hypothesis,
        IReadOnlyList<BookCandidate> candidates)
    {
        var summary = string.Join("\n", candidates.Select((c, i) =>
            $"{i + 1}. id={c.Book.WorkId} | title=\"{c.Book.Title}\" | authors={string.Join(", ", c.Book.PrimaryAuthors)} | year={c.Book.FirstPublishYear?.ToString() ?? "?"} | tier={c.Tier}"));

        var prompt = $$"""
        You are reranking book candidates that a deterministic matcher already shortlisted.
        Return the candidates ordered by how well they match the user's intent, using only the
        information provided. Do not add, remove, or invent candidates.

        Original query: "{{originalQuery}}"
        Extracted hypothesis: title={{hypothesis.Title ?? "null"}}, author={{hypothesis.Author ?? "null"}}, year={{(hypothesis.Year?.ToString() ?? "null")}}, keywords=[{{string.Join(", ", hypothesis.Keywords)}}]

        Candidates:
        {{summary}}

        Output strictly this JSON shape (no markdown):
        {
          "order": ["<workId-best>", "<workId-second>", ...]
        }

        The "order" array must be a permutation of the ids above. If you are unsure, keep the input order.
        """;

        return new GeminiRequest
        {
            Contents = { new GeminiContent { Parts = { new GeminiPart { Text = prompt } } } },
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = 0.0,
                MaxOutputTokens = _options.MaxOutputTokens,
                ResponseSchema = RerankSchema,
            },
        };
    }

    private ExtractedBookInfo ParseExtraction(string text)
    {
        var cleaned = StripMarkdownFences(text);

        try
        {
            var payload = JsonSerializer.Deserialize<GeminiExtractionPayload>(cleaned, JsonOptions);
            if (payload is null)
            {
                return ExtractedBookInfo.Empty;
            }

            var keywords = (payload.Keywords ?? new List<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();

            return new ExtractedBookInfo(
                NormalizeNullable(payload.Title),
                NormalizeNullable(payload.Author),
                ValidateYear(payload.Year),
                keywords);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Gemini returned unparseable JSON: {Text}", text);
            return ExtractedBookInfo.Empty;
        }
    }

    private IReadOnlyList<string> ParseRerankOrder(string text)
    {
        var cleaned = StripMarkdownFences(text);
        try
        {
            var payload = JsonSerializer.Deserialize<GeminiRerankPayload>(cleaned, JsonOptions);
            return (payload?.Order ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Gemini rerank returned unparseable JSON: {Text}", text);
            return Array.Empty<string>();
        }
    }

    private static string StripMarkdownFences(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            cleaned = cleaned.Trim('`');
            var newlineIdx = cleaned.IndexOf('\n');
            if (newlineIdx >= 0 && cleaned[..newlineIdx].Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[(newlineIdx + 1)..];
            }
            cleaned = cleaned.Trim('`').Trim();
        }
        return cleaned;
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Reject implausible years (e.g. "195" missing a digit, "2950" a hallucination).
    // Books older than printing are not in Open Library; anything after next year is
    // almost certainly a model mistake.
    private static int? ValidateYear(int? year)
    {
        if (year is null) return null;
        var max = DateTime.UtcNow.Year + 1;
        return year is >= 1400 && year.Value <= max ? year : null;
    }

    // Dead-simple rule-based extractor used when the LLM is unavailable. Not
    // meant to compete with Gemini — just keeps the app usable (and the demo
    // honest) instead of returning Empty for every query.
    internal static ExtractedBookInfo HeuristicExtract(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ExtractedBookInfo.Empty;
        }

        var yearMatch = YearRegex().Match(query);
        int? year = yearMatch.Success && int.TryParse(yearMatch.Value, out var y) ? y : null;

        var withoutYear = year is null ? query : query.Replace(yearMatch.Value, string.Empty);
        var tokens = withoutYear
            .Split(new[] { ' ', ',', ';', ':', '.', '!', '?', '(', ')', '[', ']', '\t', '\n', '\r' },
                   StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .ToArray();

        // Heuristic author: trailing sequence of capitalized tokens (e.g. "mark twain").
        // Falls back to the first Capitalized token if nothing at the tail qualifies.
        var capitalized = tokens.Select((t, i) => (t, i)).Where(p => char.IsUpper(p.t[0])).ToArray();
        string? author = null;
        if (capitalized.Length >= 2 && capitalized[^1].i == tokens.Length - 1 && capitalized[^2].i == tokens.Length - 2)
        {
            author = $"{capitalized[^2].t} {capitalized[^1].t}";
            tokens = tokens.Take(tokens.Length - 2).ToArray();
        }
        else if (capitalized.Length >= 1)
        {
            author = capitalized[0].t;
            tokens = tokens.Where((_, i) => i != capitalized[0].i).ToArray();
        }

        var title = tokens.Length > 0 ? string.Join(' ', tokens) : null;
        return new ExtractedBookInfo(
            NormalizeNullable(title),
            NormalizeNullable(author),
            year,
            Array.Empty<string>());
    }

    [GeneratedRegex(@"\b(1[4-9]\d{2}|20\d{2})\b")]
    private static partial Regex YearRegex();
}
