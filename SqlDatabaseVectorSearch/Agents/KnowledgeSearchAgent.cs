using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SqlDatabaseVectorSearch.Agents;

/// <summary>
/// An <see cref="AIAgent"/> that performs the whole knowledge search task: it reformulates the incoming question using the conversation context and
/// then delegates the run to the actual RAG agent, so that callers can get both the reformulation and the answer with a single invocation.
/// </summary>
/// <param name="innerAgent">The RAG agent that actually searches the knowledge base and answers the question.</param>
/// <param name="reformulationAgent">The agent that rewrites the question into a self-contained one.</param>
/// <remarks>
/// <para>
/// The reformulation is executed on the very same <see cref="AgentSession"/> that is used by the RAG agent: the reformulation agent needs
/// the conversation context to resolve pronouns and implicit subjects, while its own request and response messages are discarded by its
/// <see cref="ChatHistoryProvider"/>. In this way the session keeps containing only the reformulated questions and the related answers.
/// </para>
/// <para>
/// The outcome of the reformulation is exposed through the <see cref="AgentResponse.AdditionalProperties"/> of the response (and through the
/// first <see cref="AgentResponseUpdate"/> when streaming), so that no information is lost when collapsing the two runs into one.
/// </para>
/// </remarks>
public sealed class KnowledgeSearchAgent(AIAgent innerAgent, AIAgent reformulationAgent) : DelegatingAIAgent(innerAgent)
{
    internal const string ReformulatedQuestionPropertyName = "reformulatedQuestion";
    internal const string ReformulationUsagePropertyName = "reformulationUsage";

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        var reformulation = await ReformulateAsync(messages, session, options, cancellationToken);

        var response = await base.RunCoreAsync(reformulation.Messages, session, options, cancellationToken);
        response.AdditionalProperties = CreateProperties(reformulation, response.AdditionalProperties);

        return response;
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reformulation = await ReformulateAsync(messages, session, options, cancellationToken);

        // The first update carries no content and is used only to notify the reformulation outcome before the answer starts streaming.
        yield return new AgentResponseUpdate
        {
            AdditionalProperties = CreateProperties(reformulation)
        };

        await foreach (var update in base.RunCoreStreamingAsync(reformulation.Messages, session, options, cancellationToken))
        {
            yield return update;
        }
    }

    private async Task<Reformulation> ReformulateAsync(IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken)
    {
        if (options is KnowledgeSearchAgentRunOptions { Reformulate: false })
        {
            // The caller has explicitly requested to skip the reformulation step, so we just return the original messages and no reformulated question.
            return new(messages);
        }

        var response = await reformulationAgent.RunAsync(messages, session, cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            // The reformulation agent returned an empty response, so we just return the original messages and no reformulated question.
            return new(messages, ReformulatedQuestion: null, response.Usage);
        }

        return new([new ChatMessage(ChatRole.User, response.Text)], ReformulatedQuestion: response.Text, response.Usage);
    }

    private static AdditionalPropertiesDictionary CreateProperties(Reformulation reformulation, AdditionalPropertiesDictionary? properties = null)
    {
        properties ??= [];
        properties[ReformulatedQuestionPropertyName] = reformulation.ReformulatedQuestion;
        properties[ReformulationUsagePropertyName] = reformulation.Usage;

        return properties;
    }

    private sealed record class Reformulation(IEnumerable<ChatMessage> Messages, string? ReformulatedQuestion = null, UsageDetails? Usage = null);
}
