namespace SqlDatabaseVectorSearch.Models;

public record class DocumentChunk(Guid Id, int Index, string Content, int? PageNumber, int IndexOnPage, float[]? Embedding = null);
