namespace SqlDatabaseVectorSearch.Models;

// Question and Asnwer can be null when using response streaming.
public record class Response(string? Question, string? Answer, StreamState? StreamState = null);