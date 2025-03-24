namespace SqlDatabaseVectorSearch.Models;

public record class TokenUsage(int PromptTokens, int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}
