namespace SqlDatabaseVectorSearch.Models;

public record class TokenUsageResponse(TokenUsage? Reformulation, int? EmbeddingTokenCount, TokenUsage? Question);
