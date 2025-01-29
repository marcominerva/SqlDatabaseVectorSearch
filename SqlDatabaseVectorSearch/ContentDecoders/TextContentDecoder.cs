namespace SqlDatabaseVectorSearch.ContentDecoders;

public class TextContentDecoder : IContentDecoder
{
    public async Task<string> DecodeAsync(Stream stream, string contentType)
    {
        using var readStream = new StreamReader(stream);
        var content = await readStream.ReadToEndAsync();

        return content;
    }
}
