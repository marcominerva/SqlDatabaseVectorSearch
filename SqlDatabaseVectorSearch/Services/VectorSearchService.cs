using System.Data;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Embeddings;
using SqlDatabaseVectorSearch.ContentDecoders;
using SqlDatabaseVectorSearch.DataAccessLayer;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Settings;
using SqlDatabaseVectorSearch.TextChunkers;
using Entities = SqlDatabaseVectorSearch.DataAccessLayer.Entities;

namespace SqlDatabaseVectorSearch.Services;

public class VectorSearchService(IServiceProvider serviceProvider, ApplicationDbContext dbContext, DocumentService documentService, ITextEmbeddingGenerationService textEmbeddingGenerationService, TokenizerService tokenizerService, ChatService chatService, TimeProvider timeProvider, IOptions<AppSettings> appSettingsOptions, ILogger<VectorSearchService> logger)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public async Task<ImportDocumentResponse> ImportAsync(Stream stream, string name, string contentType, Guid? documentId, CancellationToken cancellationToken = default)
    {
        // Extract the contents of the file.
        var decoder = serviceProvider.GetKeyedService<IContentDecoder>(contentType) ?? throw new NotSupportedException($"Content type '{contentType}' is not supported.");
        var content = await decoder.DecodeAsync(stream, contentType, cancellationToken);

        // We get the token count of the whole document because it is the total number of token used by embedding (it may be necessary, for example, for cost analysis).
        var tokenCount = tokenizerService.CountEmbeddingTokens(content);

        var strategy = dbContext.Database.CreateExecutionStrategy();
        var document = await strategy.ExecuteAsync(async (cancellationToken) =>
        {
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (documentId.HasValue)
            {
                // If the user is importing a document that already exists, delete the previous one.
                await documentService.DeleteAsync(documentId.Value, cancellationToken);
            }

            var document = new Entities.Document { Id = documentId.GetValueOrDefault(), Name = name, CreationDate = timeProvider.GetUtcNow() };
            dbContext.Documents.Add(document);

            // Split the content into chunks and generate the embeddings for each one.
            var textChunker = serviceProvider.GetRequiredKeyedService<ITextChunker>(contentType);
            var paragraphs = textChunker.Split(content);
            var embeddings = await textEmbeddingGenerationService.GenerateEmbeddingsAsync(paragraphs, cancellationToken: cancellationToken);

            // Save the document chunks and the corresponding embedding in the database.
            foreach (var (index, paragraph) in paragraphs.Index())
            {
                logger.LogDebug("Storing a paragraph of {TokenCount} tokens.", tokenizerService.CountChatCompletionTokens(paragraph));

                var documentChunk = new Entities.DocumentChunk { Document = document, Index = index, Content = paragraph!, Embedding = embeddings[index].ToArray() };
                dbContext.DocumentChunks.Add(documentChunk);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.Database.CommitTransactionAsync(cancellationToken);

            return document;
        }, cancellationToken);

        return new(document.Id, tokenCount);
    }

    private async Task<(ChatResponse ReformulatedQuestion, int EmbeddingTokenCount, IEnumerable<string> Chunks)> CreateContextAsync(Question question, bool reformulate, CancellationToken cancellationToken)
    {
        // Reformulate the question taking into account the context of the chat to perform keyword search and embeddings.
        var reformulatedQuestion = reformulate ? await chatService.CreateQuestionAsync(question.ConversationId, question.Text, cancellationToken) : new(question.Text);

        var embeddingTokenCount = tokenizerService.CountEmbeddingTokens(reformulatedQuestion.Text!);
        logger.LogDebug("Embedding Token Count: {EmbeddingTokenCount}", embeddingTokenCount);

        // Generate embedding for vector search
        var questionEmbedding = await textEmbeddingGenerationService.GenerateEmbeddingAsync(reformulatedQuestion.Text!, cancellationToken: cancellationToken);

        // Perform full-text search
        var fulltextChunks = await dbContext.DocumentChunks
            .Where(c => EF.Functions.FreeText(c.Content, reformulatedQuestion.Text!))
            .Take(appSettings.MaxRelevantChunks)
            .ToListAsync(cancellationToken);

        // Perform vector similarity search
        var vectorChunks = await dbContext.DocumentChunks
            .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, questionEmbedding.ToArray()))
            .Take(appSettings.MaxRelevantChunks)
            .ToListAsync(cancellationToken);

        //Combine results with unique Ids
        var combinedChunks = fulltextChunks
            .Union(vectorChunks)
            .DistinctBy(c => c.Id)
            .Select(c => c.Content)
            .ToList();
        
        return (reformulatedQuestion, embeddingTokenCount, combinedChunks);
    }
}