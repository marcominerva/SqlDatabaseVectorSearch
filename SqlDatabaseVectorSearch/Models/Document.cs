namespace SqlDatabaseVectorSearch.Models;

public record class Document(Guid Id, string Name, DateTimeOffset CreationDate, int ChunkCount);
