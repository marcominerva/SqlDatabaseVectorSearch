namespace SqlDatabaseVectorSearch.Models;

public record class Citation(Guid DocumentId, Guid ChunkId, string FileName, string Quote, int? PageNumber, int IndexOnPage);
