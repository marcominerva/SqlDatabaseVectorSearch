namespace SqlDatabaseVectorSearch.Models;

public record class TokenUsageResponse(TokenUsage? Reformulation, int? EmbeddingTokenCount, TokenUsage? Question)
{
    public TokenUsageResponse(TokenUsage? question)
        : this(null, null, question)
    {
    }
}
