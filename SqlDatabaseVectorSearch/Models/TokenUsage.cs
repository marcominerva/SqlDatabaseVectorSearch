namespace SqlDatabaseVectorSearch.Models;

public record class TokenUsage(int InputTokenCount, int OutputTokenCount)
{
    public int TotalTokenCount => InputTokenCount + OutputTokenCount;
}
