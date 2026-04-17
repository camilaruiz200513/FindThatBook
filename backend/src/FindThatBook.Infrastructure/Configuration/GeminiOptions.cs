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
}
