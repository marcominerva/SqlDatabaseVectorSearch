using System.Data;
using Microsoft.EntityFrameworkCore;
using SqlDatabaseVectorSearch.DataAccessLayer;
using SqlDatabaseVectorSearch.Models;

namespace SqlDatabaseVectorSearch.Services;

public class DocumentService(ApplicationDbContext dbContext)
{
    public async Task<IEnumerable<Document>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var documents = await dbContext.Documents.OrderBy(d => d.Name)
            .Select(d => new Document(d.Id, d.Name, d.CreationDate, d.Chunks.Count))
            .ToListAsync(cancellationToken: cancellationToken);

        return documents;
    }

    public async Task<IEnumerable<DocumentChunk>> GetDocumentChunksAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var documentChunks = await dbContext.DocumentChunks.Where(c => c.DocumentId == documentId).OrderBy(c => c.Index)
            .Select(c => new DocumentChunk(c.Id, c.Index, c.Content, null))
            .ToListAsync(cancellationToken: cancellationToken);

        return documentChunks;
    }

    public async Task<DocumentChunk?> GetDocumentChunkEmbeddingAsync(Guid documentId, Guid documentChunkId, CancellationToken cancellationToken = default)
    {
        var documentChunk = await dbContext.DocumentChunks.Where(c => c.Id == documentChunkId && c.DocumentId == documentId)
            .Select(c => new DocumentChunk(c.Id, c.Index, c.Content, c.Embedding))
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);

        return documentChunk;
    }

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
            => dbContext.Documents.Where(d => d.Id == documentId).ExecuteDeleteAsync(cancellationToken: cancellationToken);
}