using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Caching.Hybrid;

namespace SqlDatabaseVectorSearch.Services;

public class HybridCacheSessionStoreService(HybridCache cache) : AgentSessionStore
{
    public override async ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string conversationId, CancellationToken cancellationToken = default)
    {
        var sessionContent = await cache.GetOrCreateAsync(
            GetCacheKey(conversationId),
            async ct =>
            {
                var session = await agent.CreateSessionAsync(ct);
                return await agent.SerializeSessionAsync(session, cancellationToken: ct);
            },
            cancellationToken: cancellationToken);

        return await agent.DeserializeSessionAsync(sessionContent, cancellationToken: cancellationToken);
    }

    public override async ValueTask SaveSessionAsync(AIAgent agent, string conversationId, AgentSession session, CancellationToken cancellationToken = default)
    {
        var sessionContent = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        await cache.SetAsync(GetCacheKey(conversationId), sessionContent, cancellationToken: cancellationToken);
    }

    private static string GetCacheKey(string conversationId) => $"agent-session:{conversationId}";
}
