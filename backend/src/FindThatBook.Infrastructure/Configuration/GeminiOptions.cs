namespace FindThatBook.Infrastructure.Configuration;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "gemini-2.5-flash";
    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta";
    public int TimeoutSeconds { get; init; } = 20;
    public double Temperature { get; init; } = 0.1;
    // Gemini 2.5 counts internal "thinking" tokens toward this budget, so 512
    // leaves ~200 tokens of real output headroom after reasoning.
    public int MaxOutputTokens { get; init; } = 512;

    /// <summary>
    /// Nucleus-sampling cutoff. Supported by Gemini 2.5 via <c>generationConfig.topP</c>.
    /// Leave null to defer to the model's default; set to narrow the sampling tail.
    /// </summary>
    public double? TopP { get; init; } = 0.95;

    /// <summary>
    /// Fixed sampling seed. Supported by Gemini 2.5 via <c>generationConfig.seed</c> for
    /// reproducibility across runs and reviewer demos. Leave null to let the server pick one.
    /// </summary>
    public int? Seed { get; init; } = 42;
}
