using System.Data;
using Microsoft.EntityFrameworkCore;
using SqlDatabaseVectorSearch.DataAccessLayer;
using SqlDatabaseVectorSearch.Models;

namespace SqlDatabaseVectorSearch.Services;

public class DocumentService(ApplicationDbContext dbContext)
{
    public async Task<IEnumerable<Document>> GetDocumentsAsync()
    {
        var documents = await dbContext.Documents.OrderBy(d => d.Name)
            .Select(d => new Document(d.Id, d.Name, d.CreationDate, d.Chunks.Count))
            .ToListAsync();

        return documents;
    }

    public async Task<IEnumerable<DocumentChunk>> GetDocumentChunksAsync(Guid documentId)
    {
        var documentChunks = await dbContext.DocumentChunks.Where(c => c.DocumentId == documentId).OrderBy(c => c.Index)
            .Select(c => new DocumentChunk(c.Id, c.Index, c.Content, null))
            .ToListAsync();

        return documentChunks;
    }

    public async Task<DocumentChunk?> GetDocumentChunkEmbeddingAsync(Guid documentId, Guid documentChunkId)
    {
        var documentChunk = await dbContext.DocumentChunks.Where(c => c.Id == documentChunkId && c.DocumentId == documentId)
            .Select(c => new DocumentChunk(c.Id, c.Index, c.Content, c.Embedding))
            .FirstOrDefaultAsync();

        return documentChunk;
    }

    public Task DeleteDocumentAsync(Guid documentId)
            => dbContext.Documents.Where(d => d.Id == documentId).ExecuteDeleteAsync();
}