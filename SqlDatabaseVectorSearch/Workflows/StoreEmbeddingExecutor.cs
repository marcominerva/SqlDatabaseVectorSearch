using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using SqlDatabaseVectorSearch.Data;
using SqlDatabaseVectorSearch.Services;
using Entities = SqlDatabaseVectorSearch.Data.Entities;

namespace SqlDatabaseVectorSearch.Workflows;

public partial class StoreEmbeddingExecutor(ApplicationDbContext dbContext, DocumentService documentService, TokenizerService tokenizerService, TimeProvider timeProvider, ILogger<StoreEmbeddingExecutor> logger) : Executor(nameof(StoreEmbeddingExecutor))
{
    [MessageHandler]
    private async ValueTask<StoreEmbeddingResponse> HandleAsync(EmbeddingResponse embeddingData, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        var document = await strategy.ExecuteAsync(async (cancellationToken) =>
        {
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (embeddingData.Request.DocumentId.HasValue)
            {
                // If the user is importing a document that already exists, delete the previous one.
                await documentService.DeleteAsync(embeddingData.Request.DocumentId.Value, cancellationToken);
            }

            var document = new Entities.Document { Id = embeddingData.Request.DocumentId.GetValueOrDefault(), Name = embeddingData.Request.FileName, CreationDate = timeProvider.GetUtcNow() };
            dbContext.Documents.Add(document);

            // Save the document chunks and the corresponding embedding in the database.
            foreach (var (index, embedding) in embeddingData.Embeddings.Index())
            {
                var chunk = embeddingData.Chunks.ElementAt(index);
                logger.LogDebug("Storing a chunk of {TokenCount} tokens.", tokenizerService.CountEmbeddingTokens(chunk.Content));

                var documentChunk = new Entities.DocumentChunk
                {
                    Document = document,
                    Index = index,
                    PageNumber = chunk.PageNumber,
                    IndexOnPage = chunk.IndexOnPage,
                    Content = chunk.Content,
                    Embedding = new SqlVector<float>(embedding.Vector)
                };

                dbContext.DocumentChunks.Add(documentChunk);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.Database.CommitTransactionAsync(cancellationToken);

            return document;
        }, cancellationToken);

        return new(document.Id, embeddingData.TokenCount);
    }
}

public record class StoreEmbeddingResponse(Guid DocumentId, int TokenCount);