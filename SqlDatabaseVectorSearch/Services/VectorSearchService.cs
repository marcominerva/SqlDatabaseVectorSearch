using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Embeddings;
using SqlDatabaseVectorSearch.ContentDecoders;
using SqlDatabaseVectorSearch.DataAccessLayer;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Settings;
using Entities = SqlDatabaseVectorSearch.DataAccessLayer.Entities;

namespace SqlDatabaseVectorSearch.Services;

public class VectorSearchService(IServiceProvider serviceProvider, ApplicationDbContext dbContext, DocumentService documentService, ITextEmbeddingGenerationService textEmbeddingGenerationService, TokenizerService tokenizerService, TextChunkerService textChunkerService, ChatService chatService, TimeProvider timeProvider, IOptions<AppSettings> appSettingsOptions, ILogger<VectorSearchService> logger)
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
                await documentService.DeleteDocumentAsync(documentId.Value, cancellationToken);
            }

            var document = new Entities.Document { Id = documentId.GetValueOrDefault(), Name = name, CreationDate = timeProvider.GetUtcNow() };
            dbContext.Documents.Add(document);

            // Split the content into chunks and generate the embeddings for each one.
            var paragraphs = textChunkerService.Split(content);
            var embeddings = await textEmbeddingGenerationService.GenerateEmbeddingsAsync(paragraphs, cancellationToken: cancellationToken);

            // Save the document chunks and the corresponding embedding in the database.
            foreach (var (index, paragraph) in paragraphs.Index())
            {
                logger.LogInformation("Storing a paragraph of {TokenCount} tokens.", tokenizerService.CountChatCompletionTokens(paragraph));

                var documentChunk = new Entities.DocumentChunk { Document = document, Index = index, Content = paragraph!, Embedding = embeddings[index].ToArray() };
                dbContext.DocumentChunks.Add(documentChunk);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.Database.CommitTransactionAsync(cancellationToken);

            return document;
        }, cancellationToken);

        return new(document.Id, tokenCount);
    }

    public async Task<QuestionResponse> AskQuestionAsync(Question question, bool reformulate = true, CancellationToken cancellationToken = default)
    {
        // It the user doesn't want to reforulate the question, CreateContextAsync returns the original one.
        var (reformulatedQuestion, embeddingTokenCount, chunks) = await CreateContextAsync(question, reformulate, cancellationToken);

        var (answer, tokenUsage) = await chatService.AskQuestionAsync(question.ConversationId, chunks, reformulatedQuestion.Text!, cancellationToken);

        return new(question.Text, reformulatedQuestion.Text!, answer, null, new(reformulatedQuestion.TokenUsage, embeddingTokenCount, tokenUsage));
    }

    public async IAsyncEnumerable<QuestionResponse> AskStreamingAsync(Question question, bool reformulate = true, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // It the user doesn't want to reforulate the question, CreateContextAsync returns the original one.
        var (reformulatedQuestion, embeddingTokenCount, chunks) = await CreateContextAsync(question, reformulate, cancellationToken);

        var answerStream = chatService.AskStreamingAsync(question.ConversationId, chunks, reformulatedQuestion.Text!, cancellationToken: cancellationToken);

        // The first message contains the question and the corresponding token usage (if reformulated).
        yield return new(question.Text, reformulatedQuestion.Text!, null, StreamState.Start, new(reformulatedQuestion.TokenUsage, embeddingTokenCount, null));

        TokenUsageResponse? tokenUsageResponse = null;

        // Return each token as a partial response.
        await foreach (var (token, tokenUsage) in answerStream)
        {
            // Token usage is expected in the last message.
            tokenUsageResponse = tokenUsage is not null ? new(tokenUsage) : null;
            yield return new(token, tokenUsageResponse is null ? StreamState.Append : StreamState.End, tokenUsageResponse);
        }

        // If the token usage has not been returned in the last message, we must explicitly tells that the stream is ended.
        if (tokenUsageResponse is null)
        {
            yield return new(null, StreamState.End);
        }
    }

    private async Task<(ChatResponse ReformulatedQuestion, int EmbeddingTokenCount, IEnumerable<string> Chunks)> CreateContextAsync(Question question, bool reformulate, CancellationToken cancellationToken)
    {
        // Reformulate the question taking into account the context of the chat to perform keyword search and embeddings.
        var reformulatedQuestion = reformulate ? await chatService.CreateQuestionAsync(question.ConversationId, question.Text, cancellationToken) : new(question.Text);
        var embeddingTokenCount = tokenizerService.CountEmbeddingTokens(reformulatedQuestion.Text!);

        // Perform Vector Search on SQL Database.
        var questionEmbedding = await textEmbeddingGenerationService.GenerateEmbeddingAsync(reformulatedQuestion.Text!, cancellationToken: cancellationToken);

        var chunks = await dbContext.DocumentChunks
                    .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, questionEmbedding.ToArray()))
                    .Select(c => c.Content)
                    .Take(appSettings.MaxRelevantChunks)
                    .ToListAsync(cancellationToken);

        return (reformulatedQuestion, embeddingTokenCount, chunks);
    }
}