﻿using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using OpenAI.Chat;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Settings;
using Entities = SqlDatabaseVectorSearch.Data.Entities;

namespace SqlDatabaseVectorSearch.Services;

public class ChatService(IChatCompletionService chatCompletionService, TokenizerService tokenizerService, HybridCache cache, IOptions<AppSettings> appSettingsOptions, ILogger<ChatService> logger)
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public async Task<ChatResponse> CreateQuestionAsync(Guid conversationId, string question, CancellationToken cancellationToken = default)
    {
        var chat = await GetChatHistoryAsync(conversationId, cancellationToken);

        var embeddingQuestion = $"""
            Reformulate the following question taking into account the context of the chat to perform embeddings search:
            ---
            {question}
            ---
            The reformulation must always explicitly contain the subject of the question.
            You must reformulate the question in the same language of the user's question. For example, it the user asks a question in English, the answer must be in English.
            Never add "in this chat", "in the context of this chat", "in the context of our conversation", "search for" or something like that in your answer.
            """;

        chat.AddUserMessage(embeddingQuestion);

        var reformulatedQuestion = await chatCompletionService.GetChatMessageContentAsync(chat, cancellationToken: cancellationToken);
        chat.AddAssistantMessage(reformulatedQuestion.Content!);

        await UpdateCacheAsync(conversationId, chat, cancellationToken);

        var tokenUsage = GetTokenUsage(reformulatedQuestion);
        logger.LogDebug("Reformulation: {TokenUsage}", tokenUsage);

        return new(reformulatedQuestion.Content!, tokenUsage);
    }

    public async Task<ChatResponse> AskQuestionAsync(Guid conversationId, IEnumerable<Entities.DocumentChunk> chunks, string question, CancellationToken cancellationToken = default)
    {
        var chat = CreateChatAsync(chunks, question);

        var answer = await chatCompletionService.GetChatMessageContentAsync(chat, new AzureOpenAIPromptExecutionSettings
        {
            MaxTokens = appSettings.MaxOutputTokens
        }, cancellationToken: cancellationToken);

        // Add question and answer to the chat history.
        await SetChatHistoryAsync(conversationId, question, answer.Content!, cancellationToken);

        var tokenUsage = GetTokenUsage(answer);
        logger.LogDebug("Ask question: {TokenUsage}", tokenUsage);

        return new(answer.Content!, tokenUsage);
    }

    public async IAsyncEnumerable<ChatResponse> AskStreamingAsync(Guid conversationId, IEnumerable<Entities.DocumentChunk> chunks, string question, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chat = CreateChatAsync(chunks, question);

        var answer = new StringBuilder();
        await foreach (var token in chatCompletionService.GetStreamingChatMessageContentsAsync(chat, new AzureOpenAIPromptExecutionSettings
        {
            MaxTokens = appSettings.MaxOutputTokens
        }, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(token.Content))
            {
                yield return new(token.Content);
                answer.Append(token.Content);
            }
            else if (token.Content is null)
            {
                // Token usage is returned in the last message, when the Content is null.
                var tokenUsage = GetTokenUsage(token);
                if (tokenUsage is not null)
                {
                    logger.LogDebug("Ask streaming: {TokenUsage}", tokenUsage);
                    yield return new(null, tokenUsage);
                }
            }
        }

        // Add question and answer to the chat history.
        await SetChatHistoryAsync(conversationId, question, answer.ToString(), cancellationToken);
    }

    private static TokenUsage? GetTokenUsage(Microsoft.SemanticKernel.ChatMessageContent message)
    {
        if (message.InnerContent is ChatCompletion content && content.Usage is not null)
        {
            return new(content.Usage.InputTokenCount, content.Usage.OutputTokenCount);
        }

        return null;
    }

    private static TokenUsage? GetTokenUsage(Microsoft.SemanticKernel.StreamingChatMessageContent message)
    {
        if (message.InnerContent is StreamingChatCompletionUpdate content && content.Usage is not null)
        {
            return new(content.Usage.InputTokenCount, content.Usage.OutputTokenCount);
        }

        return null;
    }

    private ChatHistory CreateChatAsync(IEnumerable<Entities.DocumentChunk> chunks, string question)
    {
        var chat = new ChatHistory("""
            You can use only the information provided in this chat to answer questions. If you don't know the answer, reply suggesting to refine the question.

            For example, if the user asks "What is the capital of France?" and in this chat there isn't information about France, you should reply something like:
            - This information isn't available in the given context
            - I'm sorry, I don't know the answer to that question
            - I don't have that information
            - I don't know
            - Given the context, I can't answer that question
            - I'm sorry, I don't have enough information to answer that question

            Never answer questions that are not related to this chat.
            You must answer in the same language as the user's question.

            IMPORTANT - CITATION PLACEMENT AND LENGTH:
            The quote in each <citation> MUST be MAXIMUM 5 words, taken word-for-word from the search result. If the quote is longer than 5 words, your answer is INVALID.
            When you find an answer, you MUST place ALL citations ONLY at the very end of your response, never inside or between sentences.
            First provide your complete answer, then add a blank line, then list all citations.
            
            Use this XML format for citations:
            <citation filename='string' page_number='1'>exact quote here</citation>

            STRICT RULES for citations:
            - Citations MUST NEVER appear inside, before, or between sentences of your answer. They MUST be grouped together ONLY at the end, after a blank line.
            - If you include citations anywhere except at the end, your answer is WRONG and INVALID.
            - Always include the citation(s) if there are results. If you don't know the answer, do NOT include citations.
            - The quote must be max 5 words, taken word-for-word from the search result, and is the basis for why the citation is relevant. If the quote is longer than 5 words, your answer is INVALID.
            - Do NOT refer to the presence of citations; just emit these tags right at the end, with no surrounding text.
            - The citations must always be in a list at the end of the response, one after the other. Never add the citations between the actual response text or inside sentences.
            - Do NOT add any text after the citations.
            - ALWAYS leave a blank line between your answer and the first citation.
            """);

        var prompt = new StringBuilder($"""
            Answer the following question:
            ---
            {question}
            =====          
            Using the following information:

            """);

        var availableTokens = appSettings.MaxInputTokens
                              - tokenizerService.CountChatCompletionTokens(chat[0].ToString())    // System prompt.
                              - tokenizerService.CountChatCompletionTokens(prompt.ToString()) // Initial user prompt.
                              - appSettings.MaxOutputTokens;    // To ensure there is enough space for the answer.

        foreach (var chunk in chunks)
        {
            var text = $"--- {chunk.Document.Name} (Document ID: {chunk.Document.Id} | Chunk ID: {chunk.Id}) {Environment.NewLine}{chunk.Content}{Environment.NewLine}";

            var tokenCount = tokenizerService.CountChatCompletionTokens(text);
            if (tokenCount > availableTokens)
            {
                // There isn't enough space to add the current chunk.
                break;
            }

            prompt.Append(text);

            availableTokens -= tokenCount;
            if (availableTokens <= 0)
            {
                // There isn't enough space to add more chunks.
                break;
            }
        }

        chat.AddUserMessage(prompt.ToString());
        return chat;
    }

    private async Task UpdateCacheAsync(Guid conversationId, ChatHistory chat, CancellationToken cancellationToken)
    {
        if (chat.Count > appSettings.MessageLimit)
        {
            chat.RemoveRange(0, chat.Count - appSettings.MessageLimit);
        }

        await cache.SetAsync(conversationId.ToString(), chat, cancellationToken: cancellationToken);
    }

    private async Task<ChatHistory> GetChatHistoryAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var historyCache = await cache.GetOrCreateAsync(conversationId.ToString(), (cancellationToken) =>
        {
            return ValueTask.FromResult<ChatHistory>([]);
        }, cancellationToken: cancellationToken);

        var chat = new ChatHistory(historyCache);
        return chat;
    }

    private async Task SetChatHistoryAsync(Guid conversationId, string question, string answer, CancellationToken cancellationToken)
    {
        var history = await GetChatHistoryAsync(conversationId, cancellationToken);

        history.AddUserMessage(question);
        history.AddAssistantMessage(answer);

        await UpdateCacheAsync(conversationId, history, cancellationToken);
    }
}
