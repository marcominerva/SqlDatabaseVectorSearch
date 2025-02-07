using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;
using SqlDatabaseVectorSearch.Settings;

namespace SqlDatabaseVectorSearch.Services;

public class TokenizerService(IOptions<AzureOpenAISettings> settingsOptions)
{
    private readonly TiktokenTokenizer chatCompletiontokenizer = TiktokenTokenizer.CreateForModel(settingsOptions.Value.ChatCompletion.ModelId);

    private readonly TiktokenTokenizer embeddingTokenizer = TiktokenTokenizer.CreateForModel(settingsOptions.Value.Embedding.ModelId);

    public int CountChatCompletionTokens(string input)
        => chatCompletiontokenizer.CountTokens(input);

    public int CountEmbeddingTokens(string input)
        => embeddingTokenizer.CountTokens(input);
}
