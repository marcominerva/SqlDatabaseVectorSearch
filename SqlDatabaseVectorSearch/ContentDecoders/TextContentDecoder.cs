namespace SqlDatabaseVectorSearch.ContentDecoders;

public class TextContentDecoder : IContentDecoder
{
    public async Task<IEnumerable<Chunk>> DecodeAsync(Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        using var readStream = new StreamReader(stream);
        var content = await readStream.ReadToEndAsync(cancellationToken);

        return [new(1, 0, content)];
    }
}
