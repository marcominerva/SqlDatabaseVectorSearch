namespace SqlDatabaseVectorSearch.Models;

// Question and Asnwer can be null when using response streaming.
public record class QuestionResponse(string? Question, string? Answer, StreamState? StreamState = null, TokenUsageResponse? TokenUsage = null);