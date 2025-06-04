namespace SqlDatabaseVectorSearch.Models;

// Question and Answer can be null when using response streaming.
public record class Response(string? OriginalQuestion, string? ReformulatedQuestion, string? Answer, StreamState? StreamState = null, TokenUsageResponse? TokenUsage = null)
{
    public Response(string? token, StreamState streamState, TokenUsageResponse? tokenUsageResponse = null)
        : this(null, null, token, streamState, tokenUsageResponse)
    {
    }
}