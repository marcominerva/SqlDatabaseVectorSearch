namespace SqlDatabaseVectorSearch.Models;

public record class DocumentChunk(Guid Id, int Index, string Content, float[]? Embedding = null);
