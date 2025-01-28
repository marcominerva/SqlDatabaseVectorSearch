using System.Text;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using SqlDatabaseVectorSearch.Settings;

namespace SqlDatabaseVectorSearch.Services;

public class ChatService(IChatCompletionService chatCompletionService, TokenizerService tokenizerService, HybridCache cache, IOptions<AppSettings> appSettingsOptions)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public async Task<string> CreateQuestionAsync(Guid conversationId, string question)
    {
        var chat = await GetChatHistoryAsync(conversationId);

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
        var chat = CreateChatAsync(chunks, question);

        var answer = await chatCompletionService.GetChatMessageContentAsync(chat, new AzureOpenAIPromptExecutionSettings
        {
            MaxTokens = appSettings.MaxOutputTokens
        });

        // Add question and answer to the chat history.
        await SetChatHistoryAsync(conversationId, question, answer.Content!);

        return answer.Content!;
    }

    public async IAsyncEnumerable<string> AskStreamingAsync(Guid conversationId, IEnumerable<string> chunks, string question)
    {
        var chat = CreateChatAsync(chunks, question);

        var answer = new StringBuilder();
        await foreach (var token in chatCompletionService.GetStreamingChatMessageContentsAsync(chat, new AzureOpenAIPromptExecutionSettings
        {
            MaxTokens = appSettings.MaxOutputTokens
        }))
        {
            if (!string.IsNullOrEmpty(token.Content))
            {
                yield return token.Content;
                answer.Append(token.Content);
            }
        }

        // Add question and answer to the chat history.
        await SetChatHistoryAsync(conversationId, question, answer.ToString());
    }

    private ChatHistory CreateChatAsync(IEnumerable<string> chunks, string question)
    {
        var chat = new ChatHistory("""
            You can use only the information provided in this chat to answer questions. If you don't know the answer, reply suggesting to refine the question.
            For example, if the user asks "What is the capital of France?" and in this chat there isn't information about France, you should reply something like "This information isn't available in the given context".
            Never answer to questions that are not related to this chat.
            You must answer in the same language of the user's question.
            """);

        var prompt = new StringBuilder($"""
            Answer the following question:
            ---
            {question}
            =====          
            Using the following information:

            """);

        var tokensAvailable = appSettings.MaxInputTokens
                              - tokenizerService.CountTokens(chat[0].ToString())    // System prompt.
                              - tokenizerService.CountTokens(prompt.ToString()) // Initial user prompt.
                              - appSettings.MaxOutputTokens;    // To ensure there is enough space for the answer.

        foreach (var chunk in chunks)
        {
            var text = $"---{Environment.NewLine}{chunk}";

            var tokenCount = tokenizerService.CountTokens(text);
            if (tokenCount > tokensAvailable)
            {
                // There isn't enough space to add the current chunk.
                break;
            }

            prompt.Append(text);

            tokensAvailable -= tokenCount;
            if (tokensAvailable <= 0)
            {
                // There isn't enough space to add more chunks.
                break;
            }
        }

        chat.AddUserMessage(prompt.ToString());
        return chat;
    }

    private async Task UpdateCacheAsync(Guid conversationId, ChatHistory chat)
        => await cache.SetAsync(conversationId.ToString(), chat);

    private async Task<ChatHistory> GetChatHistoryAsync(Guid conversationId)
    {
        var historyCache = await cache.GetOrCreateAsync(conversationId.ToString(),
        (cancellationToken) =>
        {
            return ValueTask.FromResult<ChatHistory>([]);
        });

        var chat = new ChatHistory(historyCache);
        return chat;
    }

    private async Task SetChatHistoryAsync(Guid conversationId, string question, string answer)
    {
        var history = await GetChatHistoryAsync(conversationId);

        history.AddUserMessage(question);
        history.AddAssistantMessage(answer);

        await UpdateCacheAsync(conversationId, history);
    }
}
