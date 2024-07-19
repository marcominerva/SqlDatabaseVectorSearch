namespace SqlDatabaseVectorSearch.Settings;

public class AzureOpenAISettings
{
    public required ServiceSettings ChatCompletion { get; init; }

    public required ServiceSettings Embedding { get; init; }
}

public class ServiceSettings
{
    public required string Endpoint { get; init; }

    public required string Deployment { get; init; }

    public required string ApiKey { get; init; }
}
