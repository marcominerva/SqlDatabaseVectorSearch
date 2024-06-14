namespace SqlDatabaseVectorSearch.Settings;

public class AppSettings
{
    public int MessageLimit { get; init; }

    public TimeSpan MessageExpiration { get; init; }

    public required string StoragePath { get; init; }

    public required string VectorDbPath { get; init; }

    public required string QueuePath { get; init; }
}
