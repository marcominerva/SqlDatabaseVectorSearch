using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SqlDatabaseVectorSearch.Data;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Settings;
using SqlDatabaseVectorSearch.Workflows;
using ChatResponse = SqlDatabaseVectorSearch.Models.ChatResponse;
using Entities = SqlDatabaseVectorSearch.Data.Entities;

namespace SqlDatabaseVectorSearch.Services;

public partial class VectorSearchService([FromKeyedServices("EmbeddingWorkflow")] Workflow workflow, ApplicationDbContext dbContext, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, TokenizerService tokenizerService, ChatService chatService, TimeProvider timeProvider, IOptions<AppSettings> appSettingsOptions, ILogger<VectorSearchService> logger)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public async Task<StoreEmbeddingResponse> ImportAsync(FormFileEmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        await using var run = await InProcessExecution.RunAsync(workflow, request, cancellationToken: cancellationToken);
        var events = run.NewEvents.ToList();

        var exception = events.OfType<WorkflowErrorEvent>().Select(e => e.Exception).FirstOrDefault();
        if (exception is not null)
        {
            throw exception;
        }

        var result = events.OfType<WorkflowOutputEvent>().Select(e => e.Data).OfType<StoreEmbeddingResponse>().First();
        return result;
    }

    public async Task<Response> AskQuestionAsync(Question question, bool reformulate = true, CancellationToken cancellationToken = default)
    {
        // It the user doesn't want to reforulate the question, CreateContextAsync returns the original one.
        var (reformulatedQuestion, embeddingTokenCount, chunks) = await CreateContextAsync(question, reformulate, cancellationToken);

        var (fullAnswer, tokenUsage) = await chatService.AskQuestionAsync(question.ConversationId, chunks, reformulatedQuestion.Text!, cancellationToken);

        // Extract citations from the answer.
        var (answer, citations) = ExtractCitations(fullAnswer);

        return new(question.Text, reformulatedQuestion.Text!, answer, StreamState.End, new(reformulatedQuestion.TokenUsage, embeddingTokenCount, tokenUsage), citations);
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
        var citationsStarted = false;

        // Returns each token as a partial response.
        await foreach (var (token, tokenUsage) in answerStream)
        {
            if (token is not null) // token can be null when the stream ends. 
            {
                fullAnswer.Append(token);

                if (token.Contains('【'))
                {
                    // Citations start when we encounter a token containing a 【 character.
                    // We need to track it because we don't want to return the citations in the actual response.
                    citationsStarted = true;
                }

                if (!citationsStarted)
                {
                    yield return new(token, StreamState.Append);
                }
            }
            else
            {
                // Token usage is expected in the last message, when token is null.
                tokenUsageResponse ??= tokenUsage is not null ? new(tokenUsage) : null;
            }
        }

        // Extract citations at the end of streaming.
        var (_, citations) = ExtractCitations(fullAnswer.ToString());
        yield return new(null, StreamState.End, tokenUsageResponse, citations);
    }

    private async Task<(ChatResponse ReformulatedQuestion, int EmbeddingTokenCount, IEnumerable<Entities.DocumentChunk> Chunks)> CreateContextAsync(Question question, bool reformulate, CancellationToken cancellationToken)
    {
        // Reformulate the question taking into account the context of the chat to perform keyword search and embeddings.
        var reformulatedQuestion = reformulate ? await chatService.CreateReformulateQuestionAsync(question.ConversationId, question.Text, cancellationToken) : new(question.Text);

        var embeddingTokenCount = tokenizerService.CountEmbeddingTokens(reformulatedQuestion.Text!);
        logger.LogDebug("Embedding Token Count: {EmbeddingTokenCount}", embeddingTokenCount);

        // Perform Vector Search on SQL Database.
        var questionEmbedding = await embeddingGenerator.GenerateVectorAsync(reformulatedQuestion.Text!, cancellationToken: cancellationToken);
        var embeddingVector = new SqlVector<float>(questionEmbedding);

        var chunks = await dbContext.DocumentChunks.Include(c => c.Document)
                    .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, embeddingVector))
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
        return (cleanText, citations.OrderBy(c => c.FileName).ThenBy(c => c.PageNumber));
    }

    [GeneratedRegex(@"<citation\s+document-id=(?:""|'|)(?<documentId>[^""']*)(?:""|'|)\s+chunk-id=(?:""|'|)(?<chunkId>[^""']*)(?:""|'|)\s+filename=(?:""|'|)(?<filename>[^""']*)(?:""|'|)\s+page-number=(?:""|'|)(?<pageNumber>[^""']*)(?:""|'|)\s+index-on-page=(?:""|'|)(?<indexOnPage>[^""']*)(?:""|'|)>\s*(?<quote>.*?)\s*</citation>", RegexOptions.Singleline)]
    private static partial Regex CitationRegEx { get; }

    [GeneratedRegex(@"【.*?】", RegexOptions.Singleline)]
    private static partial Regex RemoveCitationsRegEx { get; }
}