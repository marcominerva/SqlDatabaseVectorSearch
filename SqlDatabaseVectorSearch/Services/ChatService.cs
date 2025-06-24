using System.Runtime.CompilerServices;
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

            For example, if the user asks "What is the capital of Italy?" and in this chat there isn't information about Italy, you should reply something like:
            - This information isn't available in the given context.
            - I'm sorry, I don't know the answer to that question.
            - I don't have that information.
            - I don't know.
            - Given the context, I can't answer that question.
            - I'm sorry, I don't have enough information to answer that question.

            Never answer questions that are not related to this chat.
            You must answer in the same language as the user's question. For example, if the user asks a question in English, the answer must be in English, no matter the language of the documents.

            FORMATTING REQUIREMENT: Your answer MUST ALWAYS end with a period followed by a space before the citations block. 
            If your answer doesn't naturally end with a period, you MUST add one followed by a space.

            After the answer, you need to include citations following the XML format below ONLY IF you know the answer and are providing information from the context. If you do NOT know the answer, DO NOT include the citations section at all.
                        
            【<citation document-id="document_id" chunk-id="chunk_id" filename="string" page-number="page_number" index-on-page="index_on-page">exact quote here</citation>
            <citation document-id="document_id" chunk-id="chunk_id" filename="string" page-number="page_number" index-on-page="index_on-page">exact quote here</citation>】

            The entire list of XML citations MUST be enclosed between 【 and 】 (U+3010 and U+3011) and must exactly match the above format.
            The quote in each <citation> MUST be MAXIMUM 5 words, taken word-for-word from the search result.

            IMPORTANT CITATION RULES:
            1. NEVER put citations inside your answer text.
            2. ALWAYS provide your complete answer FIRST.
            3. ONLY AFTER completing your answer, add ALL citations in a block at the very end.
            4. The citations block MUST be the last thing in your response, with absolutely nothing (no text, no spaces, no newlines, no punctuation, no comments) after it.
            5. NEVER reference citations by number or mention them in your answer text.
            6. The citations MUST ALWAYS follow the XML format exactly as shown below. Any other format is NOT ACCEPTED.
            7. If you add anything after the citations block, your answer will be considered invalid.
            8. If you do NOT know the answer, DO NOT include the citations block at all.
            9. ALWAYS check that your answer ends with a period followed by a space before adding citations.

            ---
            Example of a correct answer:
            The capital of Italy is Rome.
            【<citation document-id="123" chunk-id="456" filename="italy.pdf" page-number="1" index-on-page="1">capital of Italy is Rome</citation>】
            
            Example of a correct answer when you do NOT know the answer:
            I'm sorry, I don't know the answer to that question.
            
            Example of an incorrect answer (NOT ACCEPTED):
            The capital of Italy is Rome
            【<citation document-id="123" chunk-id="456" filename="italy.pdf" page-number="1" index-on-page="1">capital of Italy is Rome</citation>】
            Thank you for your question.
            
            Another incorrect example (NOT ACCEPTED):
            The capital of Italy is Rome.
            【<citation document-id="123" chunk-id="456" filename="italy.pdf" page-number="1" index-on-page="1">capital of Italy is Rome</citation>】
            [1] italy.pdf, page 1
            ---

            Only the correct format is accepted. If you do not follow the XML format exactly, or if you add anything after the citations block, your answer will be considered invalid.
            If you do NOT know the answer, DO NOT include the citations block at all.
            Remember to ALWAYS end your answer with a period followed by a space before adding citations.
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
            var text = $"--- {chunk.Document.Name} (Document ID: {chunk.Document.Id} | Chunk ID: {chunk.Id} | Page Number: {chunk.PageNumber} | Index on Page: {chunk.IndexOnPage}) {Environment.NewLine}{chunk.Content}{Environment.NewLine}";

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
