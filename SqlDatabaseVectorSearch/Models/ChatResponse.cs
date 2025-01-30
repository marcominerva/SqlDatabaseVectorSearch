namespace SqlDatabaseVectorSearch.Models;

public record class ChatResponse(string? Text, TokenUsage? TokenUsage = null);