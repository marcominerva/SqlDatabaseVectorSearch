using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using SqlDatabaseVectorSearch.DataAccessLayer.Entities;
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

    public async Task<string> AskQuestionAsync(Guid conversationId, IEnumerable<DocumentChunk> chunks, string question)
    {
        var chat = new ChatHistory(cache.Get<ChatHistory?>(conversationId) ?? []);

        var prompt = new StringBuilder("""
            You can use only the information provided in this chat to answer questions.
            If you don't know the answer, reply suggesting to refine the question.
            Never answer to questions that are not related to this chat.
            You must answer in the same language of the user's question.
            Using the following information:
            ---

            """);

        // TODO: Ensure that the chunks are not too long, according to the model max token.
        foreach (var result in chunks.Select(c => c.Content))
        {
            prompt.AppendLine(result);
            prompt.AppendLine("---");
        }

        prompt.AppendLine($"""
            Answer the following question:
            ---
            {question}
            """);

        chat.AddUserMessage(prompt.ToString());

        var answer = await chatCompletionService.GetChatMessageContentAsync(chat)!;
        chat.AddAssistantMessage(answer.Content!);

        await UpdateCacheAsync(conversationId, chat);

        return answer.Content!;
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
