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

    public async Task<ImportDocumentResponse> ImportAsync(Stream stream, string name, string contentType, Guid? documentId)
    {
        // Extract the contents of the file.
        var decoder = serviceProvider.GetKeyedService<IContentDecoder>(contentType) ?? throw new NotSupportedException($"Content type '{contentType}' is not supported.");
        var content = await decoder.DecodeAsync(stream, contentType);

        // We get the token count of the whole document because it is the total number of token used by embedding (it may be necessary, for example, for cost analysis).
        var tokenCount = tokenizerService.CountTokens(content);

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

        return new(document.Id, tokenCount);
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

    public async Task<QuestionResponse> AskQuestionAsync(Question question, bool reformulate = true)
    {
        // It the user doesn't want to reforulate the question, CreateContextAsync returns the original one.
        var (reformulatedQuestion, embeddingTokenCount, chunks) = await CreateContextAsync(question, reformulate);

        var (answer, tokenUsage) = await chatService.AskQuestionAsync(question.ConversationId, chunks, reformulatedQuestion.Text!);

        return new(reformulatedQuestion.Text!, answer, null, new(reformulatedQuestion.TokenUsage, embeddingTokenCount, tokenUsage));
    }

    public async IAsyncEnumerable<QuestionResponse> AskStreamingAsync(Question question, bool reformulate = true)
    {
        // It the user doesn't want to reforulate the question, CreateContextAsync returns the original one.
        var (reformulatedQuestion, embeddingTokenCount, chunks) = await CreateContextAsync(question, reformulate);

        var answerStream = chatService.AskStreamingAsync(question.ConversationId, chunks, reformulatedQuestion.Text!);

        // The first message contains the question and the corresponding token usage (if reformulated).
        yield return new(reformulatedQuestion.Text!, null, StreamState.Start, new(reformulatedQuestion.TokenUsage, embeddingTokenCount, null));

        // Return each token as a partial response.
        await foreach (var (token, tokenUsage) in answerStream)
        {
            // Token usage is contained in the last message.
            yield return new(null, token, StreamState.Append, tokenUsage is not null ? new(null, null, tokenUsage) : null);
        }

        // The last message tells the client that the stream has ended.
        yield return new(null, null, StreamState.End);
    }

    private async Task<(ChatResponse ReformulatedQuestion, int EmbeddingTokenCount, IEnumerable<string> Chunks)> CreateContextAsync(Question question, bool reformulate = true)
    {
        // Reformulate the question taking into account the context of the chat to perform keyword search and embeddings.
        var reformulatedQuestion = reformulate ? await chatService.CreateQuestionAsync(question.ConversationId, question.Text) : new(question.Text);

        // Perform Vector Search on SQL Database.
        var embeddingTokenCount = tokenizerService.CountTokens(reformulatedQuestion.Text!);
        var questionEmbedding = await textEmbeddingGenerationService.GenerateEmbeddingAsync(reformulatedQuestion.Text!);

        var chunks = await dbContext.DocumentChunks
                    .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, questionEmbedding.ToArray()))
                    .Select(c => c.Content)
                    .Take(appSettings.MaxRelevantChunks)
                    .ToListAsync();

        return (reformulatedQuestion, embeddingTokenCount, chunks);
    }
}