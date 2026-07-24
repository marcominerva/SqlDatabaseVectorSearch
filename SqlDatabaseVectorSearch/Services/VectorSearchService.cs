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
    [FromKeyedServices("RagAgent")] AgentSessionStore sessionStore)
{
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
        UsageDetails? reformulationUsage = null;
        var reformulatedQuestion = question.Text;
        var session = await sessionStore.GetSessionAsync(ragAgent, question.ConversationId.ToString(), cancellationToken);

        if (reformulate)
        {
            // Reformulates the question taking into account the context of the chat to perform keyword search and embeddings.
            var reformulationResponse = await reformulationAgent.RunAsync(question.Text, session, cancellationToken: cancellationToken);
            reformulatedQuestion = reformulationResponse.Text;
            reformulationUsage = reformulationResponse.Usage;
        }

        var response = await ragAgent.RunAsync(reformulatedQuestion, session, cancellationToken: cancellationToken);

        await sessionStore.SaveSessionAsync(ragAgent, question.ConversationId.ToString(), session, cancellationToken);

        return new(question.ConversationId, question.Text, reformulatedQuestion, response.Text, null, new TokenUsageResponse(reformulationUsage, response.Usage));
    }

    public async IAsyncEnumerable<Response> AskStreamingAsync(Question question, bool reformulate = true, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UsageDetails? reformulationUsage = null;
        var reformulatedQuestion = question.Text;
        var session = await sessionStore.GetSessionAsync(ragAgent, question.ConversationId.ToString(), cancellationToken);

        if (reformulate)
        {
            // Reformulates the question taking into account the context of the chat to perform keyword search and embeddings.
            var reformulationResponse = await reformulationAgent.RunAsync(question.Text, session, cancellationToken: cancellationToken);
            reformulatedQuestion = reformulationResponse.Text;
            reformulationUsage = reformulationResponse.Usage;
        }

        // The first message contains the question and the corresponding token usage (if reformulated).
        yield return new(question.ConversationId, question.Text, reformulatedQuestion, null, StreamState.Start, new(reformulationUsage, null));

        var updates = new List<AgentResponseUpdate>();

        await foreach (var update in ragAgent.RunStreamingAsync(reformulatedQuestion, session, cancellationToken: cancellationToken))
        {
            updates.Add(update);
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new(question.ConversationId, update.Text, StreamState.Append);
            }
        }

        await sessionStore.SaveSessionAsync(ragAgent, question.ConversationId.ToString(), session, cancellationToken);
        var response = updates.ToAgentResponse();

        yield return new(question.ConversationId, StreamState.End, new TokenUsageResponse(null, response.Usage));
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
                        RawRepresentation = new { c.Id, c.DocumentId, c.PageNumber, c.IndexOnPage, c.Content }
                    })
                    .ToListAsync(cancellationToken);

        return chunks;
    }
}