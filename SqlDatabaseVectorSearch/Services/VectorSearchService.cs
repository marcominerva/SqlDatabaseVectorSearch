using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Text;
using SqlDatabaseVectorSearch.ContentDecoders;
using SqlDatabaseVectorSearch.DataAccessLayer;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Settings;
using Entities = SqlDatabaseVectorSearch.DataAccessLayer.Entities;

namespace SqlDatabaseVectorSearch.Services;

public class VectorSearchService(IServiceProvider serviceProvider, ApplicationDbContext dbContext, ITextEmbeddingGenerationService textEmbeddingGenerationService, ChatService chatService, TokenizerService tokenizerService, TimeProvider timeProvider, IOptions<AppSettings> appSettingsOptions, ILogger<VectorSearchService> logger)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public async Task<Guid> ImportAsync(Stream stream, string name, string contentType, Guid? documentId)
    {
        // Extract the contents of the file.
        var decoder = serviceProvider.GetRequiredKeyedService<IContentDecoder>(contentType);
        var content = await decoder.DecodeAsync(stream, contentType);

        await dbContext.Database.BeginTransactionAsync();

        if (documentId.HasValue)
        {
            // If the user is importing a document that already exists, delete the previous one.
            await DeleteDocumentAsync(documentId.Value);
        }

        var document = new Entities.Document { Id = documentId.GetValueOrDefault(), Name = name, CreationDate = timeProvider.GetUtcNow() };
        dbContext.Documents.Add(document);

        // Split the content into chunks and generate the embeddings for each one.
        var lines = TextChunker.SplitPlainTextLines(content, appSettings.MaxTokensPerLine, tokenizerService.CountTokens);
        var paragraphs = TextChunker.SplitPlainTextParagraphs(lines, appSettings.MaxTokensPerParagraph, appSettings.OverlapTokens, tokenCounter: tokenizerService.CountTokens);
        var embeddings = await textEmbeddingGenerationService.GenerateEmbeddingsAsync(paragraphs);

        // Save the document chunks and the corresponding embedding in the database.
        foreach (var (index, paragraph) in paragraphs.Index())
        {
            logger.LogInformation("Storing a paragraph of {TokenCount} tokens.", tokenizerService.CountTokens(paragraph));

            var documentChunk = new Entities.DocumentChunk { Document = document, Index = index, Content = paragraph!, Embedding = embeddings[index].ToArray() };
            dbContext.DocumentChunks.Add(documentChunk);
        }

        await dbContext.SaveChangesAsync();
        await dbContext.Database.CommitTransactionAsync();

        return document.Id;
    }

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

    public async Task<Response> AskQuestionAsync(Question question, bool reformulate = true)
    {
        var (reformulatedQuestion, chunks) = await CreateContextAsync(question, reformulate);

        var answer = await chatService.AskQuestionAsync(question.ConversationId, chunks, reformulatedQuestion);
        return new Response(reformulatedQuestion, answer);
    }

    public async IAsyncEnumerable<Response> AskStreamingAsync(Question question, bool reformulate = true)
    {
        var (reformulatedQuestion, chunks) = await CreateContextAsync(question, reformulate);

        var answerStream = chatService.AskStreamingAsync(question.ConversationId, chunks, reformulatedQuestion);

        // The first message contains the original question.
        yield return new Response(reformulatedQuestion, null, StreamState.Start);

        // Return each token as a partial response.
        await foreach (var token in answerStream)
        {
            yield return new Response(null, token, StreamState.Append);
        }

        // The last message tells the client that the stream has ended.
        yield return new Response(null, null, StreamState.End);
    }

    private async Task<(string Question, IEnumerable<string> Chunks)> CreateContextAsync(Question question, bool reformulate = true)
    {
        // Reformulate the following question taking into account the context of the chat to perform keyword search and embeddings:
        var reformulatedQuestion = reformulate ? await chatService.CreateQuestionAsync(question.ConversationId, question.Text) : question.Text;

        // Perform Vector Search on SQL Database.
        var questionEmbedding = await textEmbeddingGenerationService.GenerateEmbeddingAsync(reformulatedQuestion);

        var chunks = await dbContext.DocumentChunks
                    .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, questionEmbedding.ToArray()))
                    .Select(c => c.Content)
                    .Take(appSettings.MaxRelevantChunks)
                    .ToListAsync();

        return (reformulatedQuestion, chunks);
    }
}