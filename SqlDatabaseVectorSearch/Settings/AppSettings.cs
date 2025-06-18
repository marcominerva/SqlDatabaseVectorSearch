namespace SqlDatabaseVectorSearch.Settings;

public class AppSettings
{
    public int EmbeddingBatchSize { get; init; } = 32;

    public int MaxTokensPerLine { get; init; } = 300;

    public int MaxTokensPerParagraph { get; init; } = 1000;

    public int OverlapTokens { get; init; } = 100;

    public int MaxRelevantChunks { get; init; } = 5;

    public int MaxInputTokens { get; init; } = 16385;

    public int MaxOutputTokens { get; init; } = 800;

    public TimeSpan MessageExpiration { get; init; }

    public int MessageLimit { get; set; } = 20;
}
