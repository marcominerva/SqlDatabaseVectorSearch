using SqlDatabaseVectorSearch.TextChunkers;

namespace SqlDatabaseVectorSearch.ContentDecoders;

public class TextContentDecoder(IServiceProvider serviceProvider) : IContentDecoder
{
    public async Task<IEnumerable<Chunk>> DecodeAsync(Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        var textChunker = serviceProvider.GetRequiredKeyedService<ITextChunker>(contentType);

        using var readStream = new StreamReader(stream);
        var content = await readStream.ReadToEndAsync(cancellationToken);

        var paragraphs = textChunker.Split(content.Trim());
        return paragraphs.Select((text, index) => new Chunk(null, index, text)).ToList();
    }
}
