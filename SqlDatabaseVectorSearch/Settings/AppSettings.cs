namespace SqlDatabaseVectorSearch.Settings;

public class AppSettings
{
    public int MaxTokensPerLine { get; init; } = 300;

    public int MaxTokensPerParagraph { get; init; } = 1024;

    public int OverlapTokens { get; init; } = 100;

    public int MaxRelevantChunks { get; init; } = 5;

    public int MessageLimit { get; init; }

    public TimeSpan MessageExpiration { get; init; }
}
