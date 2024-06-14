using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using SqlDatabaseVectorSearch.Settings;

namespace SqlDatabaseVectorSearch.Services;

public class ChatService(IMemoryCache cache, IChatCompletionService chatCompletionService, IOptions<AppSettings> appSettingsOptions)
{
    public async Task<string> CreateQuestionAsync(Guid conversationId, string question)
    {
        var chat = new ChatHistory(cache.Get<ChatHistory?>(conversationId) ?? []);

        var embeddingQuestion = $"""
            Reformulate the following question taking into account the context of the chat to perform embeddings search:
            ---
            {question}
            ---
            You must reformulate the question in the same language of the user's question.
            Never add "in this chat", "in the context of this chat", "in the context of our conversation", "search for" or something like that in your answer.
            """;

        chat.AddUserMessage(embeddingQuestion);
        var reformulatedQuestion = await chatCompletionService.GetChatMessageContentAsync(chat)!;
        chat.AddAssistantMessage(reformulatedQuestion.Content!);

        await UpdateCacheAsync(conversationId, chat);

        return reformulatedQuestion.Content!;
    }

    public async Task AddInteractionAsync(Guid conversationId, string question, string answer)
    {
        var chat = new ChatHistory(cache.Get<ChatHistory?>(conversationId) ?? []);

        chat.AddUserMessage(question);
        chat.AddAssistantMessage(answer);

        await UpdateCacheAsync(conversationId, chat);
    }

    private Task UpdateCacheAsync(Guid conversationId, ChatHistory chat)
    {
        if (chat.Count > appSettingsOptions.Value.MessageLimit)
        {
            chat = new ChatHistory(chat.TakeLast(appSettingsOptions.Value.MessageLimit));
        }

        cache.Set(conversationId, chat, appSettingsOptions.Value.MessageExpiration);

        return Task.CompletedTask;
    }
}
