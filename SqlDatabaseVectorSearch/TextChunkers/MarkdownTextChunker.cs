using Microsoft.Extensions.Options;
using SqlDatabaseVectorSearch.Services;
using SqlDatabaseVectorSearch.Settings;
using SqlDatabaseVectorSearch.TextChunkers.Implementations;

namespace SqlDatabaseVectorSearch.TextChunkers;

public class MarkdownTextChunker(TokenizerService tokenizerService, IOptions<AppSettings> appSettingsOptions) : ITextChunker
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    public IList<string> Split(string text)
    {
        var lines = PlainTextChunker.SplitMarkDownLines(text, appSettings.MaxTokensPerLine, tokenizerService.CountEmbeddingTokens);
        var paragraphs = PlainTextChunker.SplitMarkdownParagraphs(lines, appSettings.MaxTokensPerParagraph, appSettings.OverlapTokens, tokenCounter: tokenizerService.CountEmbeddingTokens);

        return paragraphs;
    }
}
