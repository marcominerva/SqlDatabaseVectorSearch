namespace SqlDatabaseVectorSearch.ContentDecoders;

public interface IContentDecoder
{
    Task<string> DecodeAsync(Stream stream, string contentType);
}
