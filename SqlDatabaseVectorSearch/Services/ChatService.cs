﻿using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using SqlDatabaseVectorSearch.Settings;

namespace SqlDatabaseVectorSearch.Services;

public class ChatService(IMemoryCache cache, IChatCompletionService chatCompletionService, IOptions<AppSettings> appSettingsOptions)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

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

    public async Task<string> AskQuestionAsync(Guid conversationId, IEnumerable<string> chunks, string question)
    {
        var chat = new ChatHistory("""
            You can use only the information provided in this chat to answer questions. If you don't know the answer, reply suggesting to refine the question.
            For example, if the user asks "What is the capital of France?" and in this chat there isn't information about France, you should reply something like "This information isn't available in the given context".
            Never answer to questions that are not related to this chat.
            You must answer in the same language of the user's question.
            """);

        var prompt = new StringBuilder("""
            Using the following information:

            """);

        // TODO: Ensure that chunks are not too long, according to the model max token.
        foreach (var text in chunks)
        {
            prompt.AppendLine("---");
            prompt.Append(text);
        }

        prompt.AppendLine($"""

            =====
            Answer the following question:
            ---
            {question}
            """);

        chat.AddUserMessage(prompt.ToString());

        var answer = await chatCompletionService.GetChatMessageContentAsync(chat)!;

        // Add question and answer to the chat history.
        var history = new ChatHistory(cache.Get<ChatHistory?>(conversationId) ?? []);
        history.AddUserMessage(question);
        history.AddAssistantMessage(answer.Content!);

        await UpdateCacheAsync(conversationId, history);

        return answer.Content!;
    }

    private Task UpdateCacheAsync(Guid conversationId, ChatHistory chat)
    {
        if (chat.Count > appSettings.MessageLimit)
        {
            chat = new ChatHistory(chat.TakeLast(appSettings.MessageLimit));
        }

        cache.Set(conversationId, chat, appSettings.MessageExpiration);
        return Task.CompletedTask;
    }
}
