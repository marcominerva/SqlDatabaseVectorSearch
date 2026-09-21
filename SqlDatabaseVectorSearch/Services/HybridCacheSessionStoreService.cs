using Microsoft.Agents.AI;
using Microsoft.Extensions.Caching.Hybrid;

namespace SqlDatabaseVectorSearch.Services;

public class HybridCacheSessionStoreService(HybridCache cache) : AgentSessionStore
{
    public override async ValueTask<AgentSession?> GetSessionAsync(AIAgent agent, AgentSessionStoreKey key, CancellationToken cancellationToken = default)
    {
        var conversationId = GetKey(agent, key);
        var sessionContent = await cache.GetOrCreateAsync(conversationId, async ct =>
        {
            var session = await agent.CreateSessionAsync(ct);
            return await agent.SerializeSessionAsync(session, cancellationToken: ct);
        }, cancellationToken: cancellationToken);

        return await agent.DeserializeSessionAsync(sessionContent, cancellationToken: cancellationToken);
    }

    public override async ValueTask SaveSessionAsync(AIAgent agent, AgentSessionStoreKey key, AgentSession session, CancellationToken cancellationToken = default)
    {
        var conversationId = GetKey(agent, key);
        var sessionContent = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);

        await cache.SetAsync(conversationId, sessionContent, cancellationToken: cancellationToken);
    }

    public string GetKey(AIAgent agent, AgentSessionStoreKey key)
    {
        if (key.Partitions?.TryGetValue("isolation", out var isolationKey) == true)
        {
            return $"{agent.Id}:{isolationKey}:{key.SessionId}";
        }

        return $"{agent.Id}:{key.SessionId}";
    }
}