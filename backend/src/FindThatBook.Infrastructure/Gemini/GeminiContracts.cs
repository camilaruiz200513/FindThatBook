using System.Text.Json.Serialization;

namespace FindThatBook.Infrastructure.Gemini;

internal sealed class GeminiRequest
{
    [JsonPropertyName("contents")]
    public List<GeminiContent> Contents { get; set; } = new();

    [JsonPropertyName("generationConfig")]
    public GeminiGenerationConfig? GenerationConfig { get; set; }
}

internal sealed class GeminiContent
{
    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; set; } = new();
}

internal sealed class GeminiPart
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

internal sealed class GeminiGenerationConfig
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("maxOutputTokens")]
    public int MaxOutputTokens { get; set; }

    /// <summary>
    /// Nucleus-sampling probability cutoff. Supported by Gemini 2.5 via generationConfig.
    /// Nullable so it is omitted from the payload when not configured.
    /// </summary>
    [JsonPropertyName("topP")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    /// <summary>
    /// Fixed RNG seed. Supported by Gemini 2.5 via generationConfig for reproducibility.
    /// Nullable so it is omitted from the payload when not configured.
    /// </summary>
    [JsonPropertyName("seed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seed { get; set; }

    [JsonPropertyName("responseMimeType")]
    public string ResponseMimeType { get; set; } = "application/json";

    [JsonPropertyName("responseSchema")]
    public object? ResponseSchema { get; set; }
}

internal sealed class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }
}

internal sealed class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }
}

internal sealed class GeminiExtractionPayload
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }
}

internal sealed class GeminiRerankPayload
{
    [JsonPropertyName("order")]
    public List<string>? Order { get; set; }
}
