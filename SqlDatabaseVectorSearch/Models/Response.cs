namespace SqlDatabaseVectorSearch.Models;

// Question and Answer can be null when using response streaming.
public record class Response(Guid ConversationId, string? OriginalQuestion, string? ReformulatedQuestion, string? Answer, StreamState? StreamState = null, TokenUsageResponse? TokenUsage = null)
{
    public Response(Guid conversationId, string? token, StreamState streamState, TokenUsageResponse? tokenUsageResponse = null)
        : this(conversationId, null, null, token, streamState, tokenUsageResponse)
    {
    }

    public Response(Guid conversationId, StreamState streamState, TokenUsageResponse? tokenUsageResponse = null)
    : this(conversationId, null, null, null, streamState, tokenUsageResponse)
    {
    }
}