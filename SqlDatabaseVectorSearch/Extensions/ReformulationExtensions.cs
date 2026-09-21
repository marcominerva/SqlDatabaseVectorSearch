using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SqlDatabaseVectorSearch.Agents;

namespace SqlDatabaseVectorSearch.Extensions;

/// <summary>
/// Contains extension methods to read the reformulation details that a <see cref="KnowledgeSearchAgent"/> attaches to its results.
/// </summary>
public static class ReformulationExtensions
{
    /// <summary>
    /// Tries to get the reformulation details that have been attached to the given <paramref name="response"/>.
    /// </summary>
    /// <param name="response">The response returned by a <see cref="KnowledgeSearchAgent"/>.</param>
    /// <param name="question">When this method returns <see langword="true" />, contains the reformulated question, or <see langword="null" /> if the question has not been reformulated.</param>
    /// <param name="usage">When this method returns <see langword="true" />, contains the token usage of the reformulation, if available.</param>
    /// <returns><see langword="true" /> if the response comes from a <see cref="KnowledgeSearchAgent"/>; otherwise, <see langword="false" />.</returns>
    public static bool TryGetReformulation(this AgentResponse response, out string? question, out UsageDetails? usage)
        => TryGetReformulation(response.AdditionalProperties, out question, out usage);

    /// <summary>
    /// Tries to get the reformulation details that have been attached to the given <paramref name="update"/>.
    /// </summary>
    /// <param name="update">The streaming update returned by a <see cref="KnowledgeSearchAgent"/>.</param>
    /// <param name="question">When this method returns <see langword="true" />, contains the reformulated question, or <see langword="null" /> if the question has not been reformulated.</param>
    /// <param name="usage">When this method returns <see langword="true" />, contains the token usage of the reformulation, if available.</param>
    /// <returns><see langword="true" /> if the update is the one that carries the reformulation outcome; otherwise, <see langword="false" />.</returns>
    public static bool TryGetReformulation(this AgentResponseUpdate update, out string? question, out UsageDetails? usage)
        => TryGetReformulation(update.AdditionalProperties, out question, out usage);

    private static bool TryGetReformulation(AdditionalPropertiesDictionary? properties, out string? question, out UsageDetails? usage)
    {
        question = null;
        usage = null;

        if (properties?.ContainsKey(KnowledgeSearchAgent.ReformulatedQuestionPropertyName) is not true)
        {
            return false;
        }

        properties.TryGetValue(KnowledgeSearchAgent.ReformulatedQuestionPropertyName, out question);
        properties.TryGetValue(KnowledgeSearchAgent.ReformulationUsagePropertyName, out usage);

        return true;
    }
}
