using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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

public partial class VectorSearchService(IServiceProvider serviceProvider, ApplicationDbContext dbContext, DocumentService documentService, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, TokenizerService tokenizerService, ChatService chatService, TimeProvider timeProvider, IOptions<AppSettings> appSettingsOptions, ILogger<VectorSearchService> logger)
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

        var (fullAnswer, tokenUsage) = await chatService.AskQuestionAsync(question.ConversationId, chunks, reformulatedQuestion.Text!, cancellationToken);

        // Extract citations from the answer
        var (answer, citations) = ExtractCitations(fullAnswer);

        return new(question.Text, reformulatedQuestion.Text!, answer, null, new(reformulatedQuestion.TokenUsage, embeddingTokenCount, tokenUsage), citations);
    }

    public async IAsyncEnumerable<Response> AskStreamingAsync(Question question, bool reformulate = true, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // It the user doesn't want to reforulate the question, CreateContextAsync returns the original one.
        var (reformulatedQuestion, embeddingTokenCount, chunks) = await CreateContextAsync(question, reformulate, cancellationToken);

        var answerStream = chatService.AskStreamingAsync(question.ConversationId, chunks, reformulatedQuestion.Text!, cancellationToken: cancellationToken);

        // The first message contains the question and the corresponding token usage (if reformulated).
        yield return new(question.Text, reformulatedQuestion.Text!, null, StreamState.Start, new(reformulatedQuestion.TokenUsage, embeddingTokenCount, null));

        TokenUsageResponse? tokenUsageResponse = null;
        var fullAnswer = new StringBuilder();
        var areCitationsStarted = false;

        // Return each token as a partial response.
        await foreach (var (token, tokenUsage) in answerStream)
        {
            fullAnswer.Append(token);

            if (token?.Contains('【') == true)
            {
                // Citations are started when the first token contains a 【 character.
                // We need to track it because we don't want to return the citations in the actual response.
                areCitationsStarted = true;
            }

            if (!areCitationsStarted)
            {
                yield return new(token, StreamState.Append);
            }

            // Token usage is expected in the last message.
            tokenUsageResponse = tokenUsage is not null ? new(tokenUsage) : null;
            if (tokenUsageResponse is not null)
            {
                // Response is complete, we can return the citations.
                var (_, citations) = ExtractCitations(fullAnswer.ToString());
                yield return new(null, StreamState.End, tokenUsageResponse, citations);
            }
        }

        // If the token usage has not been returned in the last message, we must explicitly tell that the stream is ended.
        if (tokenUsageResponse is null)
        {
            // Extract citations at the end of streaming.
            var (_, citations) = ExtractCitations(fullAnswer.ToString());
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

    private static (string, IEnumerable<Citation>) ExtractCitations(string? text)
    {
        var citations = new List<Citation>();

        if (string.IsNullOrEmpty(text))
        {
            return (text ?? string.Empty, citations);
        }

        var matches = CitationRegEx.Matches(text);

        foreach (Match match in matches)
        {
            if (match.Success)
            {
                citations.Add(new Citation
                {
                    DocumentId = Guid.Parse(match.Groups["documentId"].Value),
                    ChunkId = Guid.Parse(match.Groups["chunkId"].Value),
                    FileName = match.Groups["filename"].Value,
                    PageNumber = int.TryParse(match.Groups["pageNumber"].Value, out var pageNumber) && pageNumber > 0 ? pageNumber : null,
                    IndexOnPage = int.TryParse(match.Groups["indexOnPage"].Value, out var indexOnPage) ? indexOnPage : 0,
                    Quote = match.Groups["quote"].Value
                });
            }
        }

        // Remove all content between 【 and 】.
        var cleanText = RemoveCitationsRegEx.Replace(text, string.Empty).TrimEnd();
        return (cleanText, citations);
    }

    [GeneratedRegex(@"<citation\s+document-id=(?:""|'|)(?<documentId>[^""']*)(?:""|'|)\s+chunk-id=(?:""|'|)(?<chunkId>[^""']*)(?:""|'|)\s+filename=(?:""|'|)(?<filename>[^""']*)(?:""|'|)\s+page-number=(?:""|'|)(?<pageNumber>[^""']*)(?:""|'|)\s+index-on-page=(?:""|'|)(?<indexOnPage>[^""']*)(?:""|'|)>\s*(?<quote>.*?)\s*</citation>", RegexOptions.Singleline)]
    private static partial Regex CitationRegEx { get; }

    [GeneratedRegex(@"【.*?】", RegexOptions.Singleline)]
    private static partial Regex RemoveCitationsRegEx { get; }
}