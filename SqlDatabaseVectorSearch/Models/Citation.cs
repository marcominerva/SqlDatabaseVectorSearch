namespace SqlDatabaseVectorSearch.Models;

public class Citation
{
    public Guid DocumentId { get; set; }

    public Guid ChunkId { get; set; }

    public string FileName { get; set; } = null!;

    public string Quote { get; set; } = null!;

    public int? PageNumber { get; set; }

    public int IndexOnPage { get; set; }
}