using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Caching.Hybrid;

namespace SqlDatabaseVectorSearch.Services;

public class HybridCacheSessionStoreService(HybridCache cache) : AgentSessionStore
{
    public override async ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string conversationId, CancellationToken cancellationToken = default)
    {
        var key = GetKey(agent, conversationId);
        var sessionContent = await cache.GetOrCreateAsync(key, async ct =>
        {
            var session = await agent.CreateSessionAsync(ct);
            return await agent.SerializeSessionAsync(session, cancellationToken: ct);
        }, cancellationToken: cancellationToken);

        return await agent.DeserializeSessionAsync(sessionContent, cancellationToken: cancellationToken);
    }

    public override async ValueTask SaveSessionAsync(AIAgent agent, string conversationId, AgentSession session, CancellationToken cancellationToken = default)
    {
        var key = GetKey(agent, conversationId);
        var sessionContent = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);

        await cache.SetAsync(key, sessionContent, cancellationToken: cancellationToken);
    }

    public override async ValueTask DeleteSessionAsync(AIAgent agent, string conversationId, CancellationToken cancellationToken = default)
    {
        var key = GetKey(agent, conversationId);
        await cache.RemoveAsync(key, cancellationToken);
    }

    private static string GetKey(AIAgent agent, string conversationId)
        => $"{agent.Id}:{conversationId}";
}