using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Settings;

namespace SqlDatabaseVectorSearch.Services;

public class ChatService(IMemoryCache cache, IChatCompletionService chatCompletionService, IOptions<AppSettings> appSettingsOptions)
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new(JsonSerializerDefaults.Web);

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

    public async Task<ChatResponse> AskQuestionAsync(Guid conversationId, IEnumerable<DocumentChunk> chunks, string question)
    {
        var chat = new ChatHistory("""
            You can use only the information provided in this chat to answer questions.
            Every piece of information starts with the ID of the chunk it refers.
            If you don't know the answer, reply suggesting to refine the question.
            For example, if the user asks "What is the capital of France?" and in this chat there isn't information about France, you should reply something like "This information isn't available in the given context".            
            Never answer to questions that are not related to this chat.
            You must answer in the same language of the user's question.
            The answer must be in JSON format with the following structure:
            ---
            {
                "answer": "The answer to the question.",
                "sources": [ "The list of IDs of the chunks that contain the information that have been used to provide the answer." ],
            }
            """);

        var prompt = new StringBuilder("""
            Using the following information:

            """);

        // TODO: Ensure that chunks are not too long, according to the model max token.
        foreach (var chunk in chunks)
        {
            prompt.AppendLine("---");
            prompt.AppendLine(chunk.Id.ToString());
            prompt.Append(chunk.Content);
        }

        prompt.AppendLine($"""

            =====
            Answer the following question:
            ---
            {question}
            """);

        chat.AddUserMessage(prompt.ToString());

        var responseJson = await chatCompletionService.GetChatMessageContentAsync(chat, new OpenAIPromptExecutionSettings { ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() })!;
        var response = JsonSerializer.Deserialize<ChatResponse>(responseJson.Content!, jsonSerializerOptions)!;

        // Add question and answer to the chat history.
        var history = new ChatHistory(cache.Get<ChatHistory?>(conversationId) ?? []);
        history.AddUserMessage(question);
        history.AddAssistantMessage(response.Answer);

        await UpdateCacheAsync(conversationId, history);

        return response;
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
