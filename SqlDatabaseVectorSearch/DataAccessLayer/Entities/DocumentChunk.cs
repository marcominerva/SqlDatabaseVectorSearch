namespace SqlDatabaseVectorSearch.DataAccessLayer.Entities;

public partial class DocumentChunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public required string Content { get; set; }

    public required float[] Embedding { get; set; }

    public virtual Document Document { get; set; } = null!;
}
