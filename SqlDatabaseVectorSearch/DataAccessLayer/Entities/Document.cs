namespace SqlDatabaseVectorSearch.DataAccessLayer.Entities;

public class Document
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreationDate { get; set; }

    public virtual ICollection<DocumentChunk> DocumentChunks { get; set; } = [];
}
