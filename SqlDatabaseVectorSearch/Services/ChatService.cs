using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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

        var (cleanText, citations) = ExtractCitations(answer.Content);

        // Add question and clean answer (without citations) to the chat history
        await SetChatHistoryAsync(conversationId, question, cleanText, cancellationToken);

        var tokenUsage = GetTokenUsage(answer);
        logger.LogDebug("Ask question: {TokenUsage}", tokenUsage);

        return new(cleanText, tokenUsage, citations);
    }

    public async IAsyncEnumerable<ChatResponse> AskStreamingAsync(Guid conversationId, IEnumerable<Entities.DocumentChunk> chunks, string question, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chat = CreateChatAsync(chunks, question);

        var answer = new StringBuilder();
        
        // Keep track of entire text to detect citation tags across tokens
        var runningText = new StringBuilder();
        
        // State variables for citation detection
        var insideCitationTag = false;
        var currentToken = string.Empty;

        await foreach (var token in chatCompletionService.GetStreamingChatMessageContentsAsync(chat, new AzureOpenAIPromptExecutionSettings
        {
            MaxTokens = appSettings.MaxOutputTokens
        }, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(token.Content))
            {
                currentToken = token.Content;
                answer.Append(currentToken);
                runningText.Append(currentToken);
                
                var runningTextString = runningText.ToString();
                
                // Check for citation tags in the accumulated text
                var openTagPosition = runningTextString.LastIndexOf("<citation", StringComparison.OrdinalIgnoreCase);
                var closeTagPosition = -1;
                
                if (openTagPosition >= 0)
                {
                    // We found an opening tag
                    closeTagPosition = runningTextString.IndexOf("</citation>", openTagPosition, StringComparison.OrdinalIgnoreCase);
                    
                    if (closeTagPosition < 0)
                    {
                        // We are inside a citation tag but haven't found the closing tag yet
                        insideCitationTag = true;
                    }
                }
                
                // If we're not inside a citation tag or this token doesn't contain citation tag parts,
                // send it to the UI
                if (!insideCitationTag && !currentToken.Contains("<citation", StringComparison.OrdinalIgnoreCase) && 
                    !currentToken.Contains("</citation>", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if the current token is part of a citation tag that started in previous tokens
                    if (openTagPosition >= 0 && 
                        runningTextString.Length - currentToken.Length <= openTagPosition)
                    {
                        // Skip this token as it's part of a citation tag
                    }
                    else
                    {
                        // Send the token to the UI if it's not part of a citation tag
                        yield return new(currentToken);
                    }
                }
                
                // If we find a closing tag in this iteration, reset the state
                if (closeTagPosition >= 0)
                {
                    insideCitationTag = false;
                }
            }
            else if (token.Content is null)
            {
                // Token usage is returned in the last message, when the Content is null.
                var tokenUsage = GetTokenUsage(token);

                // Always process the final answer to extract citations
                var (cleanText, citations) = ExtractCitations(answer.ToString());

                if (tokenUsage is not null)
                {
                    logger.LogDebug("Ask streaming: {TokenUsage}", tokenUsage);
                }

                yield return new(null, tokenUsage, citations);
            }
        }

        // Process the final answer to extract citations and clean the text
        var (finalCleanText, _) = ExtractCitations(answer.ToString());
        
        // Add question and clean answer to the chat history
        await SetChatHistoryAsync(conversationId, question, finalCleanText, cancellationToken);
    }

    private static (string cleanText, IEnumerable<Citation> citations) ExtractCitations(string? text)
    {
        var citations = new List<Citation>();

        if (string.IsNullOrEmpty(text))
        {
            return (text ?? string.Empty, citations);
        }

        var pattern = @"<citation\s+document-id='(?<documentId>[^']*)'\s+chunk-id='(?<chunkId>[^']*)'\s+filename='(?<filename>[^']*)'\s+page-number='(?<pageNumber>[^']*)'\s+index-on-page='(?<indexOnPage>[^']*)'>\s*(?<quote>.*?)\s*</citation>";

        var matches = Regex.Matches(text, pattern, RegexOptions.Singleline);
        foreach (Match match in matches)
        {
            if (match.Success)
            {
                citations.Add(new Citation(
                    DocumentId: Guid.Parse(match.Groups["documentId"].Value),
                    ChunkId: Guid.Parse(match.Groups["chunkId"].Value),
                    FileName: match.Groups["filename"].Value,
                    Quote: match.Groups["quote"].Value,
                    PageNumber: int.TryParse(match.Groups["pageNumber"].Value, out var pageNumber) && pageNumber > 0 ? pageNumber : null,
                    IndexOnPage: int.TryParse(match.Groups["indexOnPage"].Value, out var indexOnPage) ? indexOnPage : 0
                ));
            }
        }

        // Remove all <citation> tags from the text
        var cleanText = Regex.Replace(text, pattern, string.Empty, RegexOptions.Singleline).TrimEnd();
        return (cleanText, citations);
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

            The quote in each <citation> MUST be MAXIMUM 5 words, taken word-for-word from the search result. If the quote is longer than 5 words, your answer is INVALID.
            When you find an answer, you MUST place ALL citations ONLY at the very end of your response, never inside or between sentences.
            First provide your complete answer, then list all citations.
            
            Use this XML format for citations:
            <citation document-id='document_id' chunk-id='chunk_id' filename='string' page-number='page_number' index-on-page='index_on_page'>exact quote here</citation>
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
