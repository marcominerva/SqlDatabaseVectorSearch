using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using SqlDatabaseVectorSearch.Agents;
using SqlDatabaseVectorSearch.Extensions;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Workflows;

namespace SqlDatabaseVectorSearch.Services;

public partial class VectorSearchService([FromKeyedServices("EmbeddingWorkflow")] Workflow workflow, [FromKeyedServices("RagAgent")] AIAgent ragAgent,
    [FromKeyedServices("RagAgent")] AgentSessionStore sessionStore)
{
    public async Task<StoreEmbeddingResponse> ImportAsync(EmbeddingRequest request, CancellationToken cancellationToken = default)
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
        var session = await sessionStore.GetOrCreateSessionAsync(ragAgent, new(question.ConversationId.ToString()), cancellationToken);

        // The agent reformulates the question taking into account the context of the chat (to perform keyword search and embeddings) and then answers it.
        var response = await ragAgent.RunAsync(question.Text, session, new KnowledgeSearchAgentRunOptions { Reformulate = reformulate }, cancellationToken);

        await sessionStore.SaveSessionAsync(ragAgent, new(question.ConversationId.ToString()), session, cancellationToken);

        response.TryGetReformulation(out var reformulatedQuestion, out var reformulationUsage);

        return new(question.ConversationId, question.Text, reformulatedQuestion ?? question.Text, response.Text, null, new TokenUsageResponse(reformulationUsage, response.Usage));
    }

    public async IAsyncEnumerable<Response> AskStreamingAsync(Question question, bool reformulate = true, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var session = await sessionStore.GetOrCreateSessionAsync(ragAgent, new(question.ConversationId.ToString()), cancellationToken);
        var updates = new List<AgentResponseUpdate>();

        await foreach (var update in ragAgent.RunStreamingAsync(question.Text, session, new KnowledgeSearchAgentRunOptions { Reformulate = reformulate }, cancellationToken))
        {
            // The update that carries the reformulation is the first one and contains the question and the corresponding token usage (if reformulated).
            if (update.TryGetReformulation(out var reformulatedQuestion, out var reformulationUsage))
            {
                yield return new(question.ConversationId, question.Text, reformulatedQuestion ?? question.Text, null, StreamState.Start, new(reformulationUsage, null));
                continue;
            }

            updates.Add(update);
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new(question.ConversationId, update.Text, StreamState.Delta);
            }
        }

        await sessionStore.SaveSessionAsync(ragAgent, new(question.ConversationId.ToString()), session, cancellationToken);

        var response = updates.ToAgentResponse();

        yield return new(question.ConversationId, StreamState.End, new TokenUsageResponse(null, response.Usage));
    }
}
