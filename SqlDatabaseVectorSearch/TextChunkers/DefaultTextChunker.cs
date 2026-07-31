using Microsoft.Extensions.Options;
using SqlDatabaseVectorSearch.Services;
using SqlDatabaseVectorSearch.Settings;
using SqlDatabaseVectorSearch.TextChunkers.Implementations;

namespace SqlDatabaseVectorSearch.TextChunkers;

public class DefaultTextChunker(TokenizerService tokenizerService, IOptions<AppSettings> appSettingsOptions) : ITextChunker
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public IList<string> Split(string text)
    {
        var lines = PlainTextChunker.SplitPlainTextLines(text, appSettings.MaxTokensPerLine, tokenizerService.CountEmbeddingTokens);
        var paragraphs = PlainTextChunker.SplitPlainTextParagraphs(lines, appSettings.MaxTokensPerParagraph, appSettings.OverlapTokens, tokenCounter: tokenizerService.CountEmbeddingTokens);

        return paragraphs;
    }
}
