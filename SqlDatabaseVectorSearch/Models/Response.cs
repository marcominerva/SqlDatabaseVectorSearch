namespace SqlDatabaseVectorSearch.Models;

// Question and Answer can be null when using response streaming.
public record class Response(string? OriginalQuestion, string? ReformulatedQuestion, string? Answer, StreamState? StreamState = null, TokenUsageResponse? TokenUsage = null, IEnumerable<Citation>? Citations = null)
{
    public Response(string? token, StreamState streamState, TokenUsageResponse? tokenUsageResponse = null, IEnumerable<Citation>? citations = null)
        : this(null, null, token, streamState, tokenUsageResponse, citations)
    {
    }
}