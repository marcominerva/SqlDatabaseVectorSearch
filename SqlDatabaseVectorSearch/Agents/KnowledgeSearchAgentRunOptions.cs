using Microsoft.Agents.AI;

namespace SqlDatabaseVectorSearch.Agents;

/// <summary>
/// Provides the options that control how a <see cref="KnowledgeSearchAgent"/> handles the reformulation step of a run.
/// </summary>
public sealed class KnowledgeSearchAgentRunOptions : AgentRunOptions
{
    /// <summary>
    /// Gets a value indicating whether the question must be reformulated using the conversation context before being sent to the RAG agent
    /// (default: <see langword="true" />).
    /// </summary>
    /// <remarks>
    /// Set this property to <see langword="false" /> to send the question as-is, for example when the caller already provides a self-contained question.
    /// </remarks>
    public bool Reformulate { get; init; } = true;
}
