namespace SqlDatabaseVectorSearch.ContentDecoders;

public class TextContentDecoder : IContentDecoder
{
    public async Task<string> DecodeAsync(Stream stream, string contentType, CancellationToken cancellationToken = default)
    {
        using var readStream = new StreamReader(stream);
        var content = await readStream.ReadToEndAsync(cancellationToken);

        return content;
    }
}
