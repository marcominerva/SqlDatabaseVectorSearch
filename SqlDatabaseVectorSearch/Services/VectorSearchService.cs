using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SqlDatabaseVectorSearch.Data;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Settings;
using SqlDatabaseVectorSearch.Workflows;

namespace SqlDatabaseVectorSearch.Services;

public partial class VectorSearchService([FromKeyedServices("EmbeddingWorkflow")] Workflow workflow, [FromKeyedServices("ReformulationAgent")] AIAgent reformulationAgent, [FromKeyedServices("RagAgent")] AIAgent ragAgent,
    [FromKeyedServices("RagAgent")] AgentSessionStore sessionStore, IOptions<AppSettings> appSettingsOptions)
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

    public async Task<RagResponse> AskQuestionAsync(Question question, bool reformulate = true, CancellationToken cancellationToken = default)
    {
        var reformulatedQuestion = question.Text;
        var session = await sessionStore.GetSessionAsync(ragAgent, question.ConversationId.ToString(), cancellationToken);

        if (reformulate)
        {
            // Reformulates the question taking into account the context of the chat to perform keyword search and embeddings.
            var reformulationResponse = await reformulationAgent.RunAsync(question.Text, session, cancellationToken: cancellationToken);
            reformulatedQuestion = reformulationResponse.Text;
        }

        var response = await ragAgent.RunAsync(reformulatedQuestion, session, cancellationToken: cancellationToken);

        await sessionStore.SaveSessionAsync(ragAgent, question.ConversationId.ToString(), session, cancellationToken);

        session.TryGetInMemoryChatHistory(out var chatHistory);

        return new(question.ConversationId, question.Text, reformulatedQuestion, response.Text);
    }

    public async IAsyncEnumerable<Response> AskStreamingAsync(Question question, bool reformulate = true, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return null!;

        //// It the user doesn't want to reforulate the question, CreateContextAsync returns the original one.
        //var (reformulatedQuestion, embeddingTokenCount, chunks) = await CreateContextAsync(question, reformulate, cancellationToken);

        //var answerStream = chatService.AskStreamingAsync(question.ConversationId, chunks, reformulatedQuestion.Text!, cancellationToken: cancellationToken);

        //// The first message contains the question and the corresponding token usage (if reformulated).
        //yield return new(question.Text, reformulatedQuestion.Text!, null, StreamState.Start, new(reformulatedQuestion.TokenUsage, embeddingTokenCount, null));

        //TokenUsageResponse? tokenUsageResponse = null;
        //var fullAnswer = new StringBuilder();
        //var citationsStarted = false;

        //// Returns each token as a partial response.
        //await foreach (var (token, tokenUsage) in answerStream)
        //{
        //    if (token is not null) // token can be null when the stream ends. 
        //    {
        //        fullAnswer.Append(token);

        //        if (token.Contains('【'))
        //        {
        //            // Citations start when we encounter a token containing a 【 character.
        //            // We need to track it because we don't want to return the citations in the actual response.
        //            citationsStarted = true;
        //        }

        //        if (!citationsStarted)
        //        {
        //            yield return new(token, StreamState.Append);
        //        }
        //    }
        //    else
        //    {
        //        // Token usage is expected in the last message, when token is null.
        //        tokenUsageResponse ??= tokenUsage is not null ? new(tokenUsage) : null;
        //    }
        //}

        //// Extract citations at the end of streaming.
        //var (_, citations) = ExtractCitations(fullAnswer.ToString());
        //yield return new(null, StreamState.End, tokenUsageResponse, citations);
    }
}

public class ContextProvider(ApplicationDbContext dbContext, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, IOptions<AppSettings> appSettingsOptions)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        // Perform Vector Search on SQL Database.
        var questionEmbedding = await embeddingGenerator.GenerateVectorAsync(query, cancellationToken: cancellationToken);
        var embeddingVector = new SqlVector<float>(questionEmbedding);

        var chunks = await dbContext.DocumentChunks.Include(c => c.Document)
                    .OrderBy(c => EF.Functions.VectorDistance("cosine", c.Embedding, embeddingVector))
                    .Take(appSettings.MaxRelevantChunks).Select(c => new TextSearchProvider.TextSearchResult
                    {
                        SourceLink = c.Id.ToString().ToLowerInvariant(),
                        SourceName = c.Document.Name,
                        Text = c.Content,
                    })
                    .ToListAsync(cancellationToken);

        return chunks;
    }
}