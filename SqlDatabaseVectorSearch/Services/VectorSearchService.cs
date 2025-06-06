using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SqlDatabaseVectorSearch.ContentDecoders;
using SqlDatabaseVectorSearch.Data;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Settings;
using ChatResponse = SqlDatabaseVectorSearch.Models.ChatResponse;
using Entities = SqlDatabaseVectorSearch.Data.Entities;

namespace SqlDatabaseVectorSearch.Services;

public class VectorSearchService(IServiceProvider serviceProvider, ApplicationDbContext dbContext, DocumentService documentService, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, TokenizerService tokenizerService, ChatService chatService, TimeProvider timeProvider, IOptions<AppSettings> appSettingsOptions, ILogger<VectorSearchService> logger)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public async Task<ImportDocumentResponse> ImportAsync(Stream stream, string name, string contentType, Guid? documentId, CancellationToken cancellationToken = default)
    {
        // Extract the contents of the file.
        var decoder = serviceProvider.GetKeyedService<IContentDecoder>(contentType) ?? throw new NotSupportedException($"Content type '{contentType}' is not supported.");
        var paragraphs = await decoder.DecodeAsync(stream, contentType, cancellationToken);

        // We get the token count of the whole document because it is the total number of token used by embedding (it may be necessary, for example, for cost analysis).
        var tokenCount = tokenizerService.CountEmbeddingTokens(string.Join(" ", paragraphs.Select(p => p.Content)));

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

            var embeddings = await embeddingGenerator.GenerateAsync(paragraphs.Select(p => p.Content), cancellationToken: cancellationToken);

            // Save the document chunks and the corresponding embedding in the database.
            foreach (var (index, embedding) in embeddings.Index())
            {
                var paragraph = paragraphs.ElementAt(index);
                logger.LogDebug("Storing a paragraph of {TokenCount} tokens.", tokenizerService.CountChatCompletionTokens(paragraph.Content));

                var documentChunk = new Entities.DocumentChunk
                {
                    Document = document,
                    Index = index,
                    PageNumber = paragraph.PageNumber,
                    IndexOnPage = paragraph.IndexOnPage,
                    Content = paragraph.Content,
                    Embedding = embedding.Vector.ToArray()
                };

                dbContext.DocumentChunks.Add(documentChunk);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbContext.Database.CommitTransactionAsync(cancellationToken);

            return document;
        }, cancellationToken);

        return new(document.Id, tokenCount);
    }

    public async Task<Response> AskQuestionAsync(Question question, bool reformulate = true, CancellationToken cancellationToken = default)
    {
        // It the user doesn't want to reforulate the question, CreateContextAsync returns the original one.
        var (reformulatedQuestion, embeddingTokenCount, chunks) = await CreateContextAsync(question, reformulate, cancellationToken);

        var chatResponse = await chatService.AskQuestionAsync(question.ConversationId, chunks, reformulatedQuestion.Text!, cancellationToken);
        
        return new(
            question.Text, 
            reformulatedQuestion.Text!, 
            chatResponse.Text, 
            null, 
            new(reformulatedQuestion.TokenUsage, embeddingTokenCount, chatResponse.TokenUsage),
            chatResponse.Citations
        );
    }

    public async IAsyncEnumerable<Response> AskStreamingAsync(Question question, bool reformulate = true, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // It the user doesn't want to reforulate the question, CreateContextAsync returns the original one.
        var (reformulatedQuestion, embeddingTokenCount, chunks) = await CreateContextAsync(question, reformulate, cancellationToken);

        var answerStream = chatService.AskStreamingAsync(question.ConversationId, chunks, reformulatedQuestion.Text!, cancellationToken: cancellationToken);

        // The first message contains the question and the corresponding token usage (if reformulated).
        yield return new(
            question.Text, 
            reformulatedQuestion.Text!, 
            null, 
            StreamState.Start, 
            new(reformulatedQuestion.TokenUsage, embeddingTokenCount, null),
            null
        );

        TokenUsageResponse? tokenUsageResponse = null;
        IEnumerable<Citation>? citations = null;

        // Return each token as a partial response.
        await foreach (var chatResponse in answerStream)
        {
            // Keep track of citations if they're present
            if (chatResponse.Citations != null)
            {
                citations = chatResponse.Citations;
            }
            
            // Token usage is expected in the last message.
            tokenUsageResponse = chatResponse.TokenUsage is not null ? new(chatResponse.TokenUsage) : null;
            
            StreamState streamState = tokenUsageResponse is null ? StreamState.Append : StreamState.End;
            
            yield return new(
                chatResponse.Text, 
                streamState, 
                tokenUsageResponse,
                citations
            );
        }

        // If the token usage has not been returned in the last message, we must explicitly tells that the stream is ended.
        if (tokenUsageResponse is null)
        {
            yield return new(null, StreamState.End, null, citations);
        }
    }

    private async Task<(ChatResponse ReformulatedQuestion, int EmbeddingTokenCount, IEnumerable<Entities.DocumentChunk> Chunks)> CreateContextAsync(Question question, bool reformulate, CancellationToken cancellationToken)
    {
        // Reformulate the question taking into account the context of the chat to perform keyword search and embeddings.
        var reformulatedQuestion = reformulate ? await chatService.CreateQuestionAsync(question.ConversationId, question.Text, cancellationToken) : new(question.Text);

        var embeddingTokenCount = tokenizerService.CountEmbeddingTokens(reformulatedQuestion.Text!);
        logger.LogDebug("Embedding Token Count: {EmbeddingTokenCount}", embeddingTokenCount);

        // Perform Vector Search on SQL Database.
        var questionEmbedding = await embeddingGenerator.GenerateVectorAsync(reformulatedQuestion.Text!, cancellationToken: cancellationToken);

        var chunks = await dbContext.DocumentChunks.Include(c => c.Document)
                    .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, questionEmbedding.ToArray()))
                    .Take(appSettings.MaxRelevantChunks)
                    .ToListAsync(cancellationToken);

        return (reformulatedQuestion, embeddingTokenCount, chunks);
    }
}