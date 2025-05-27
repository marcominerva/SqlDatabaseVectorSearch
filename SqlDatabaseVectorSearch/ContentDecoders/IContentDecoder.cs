namespace SqlDatabaseVectorSearch.ContentDecoders;

public interface IContentDecoder
{
    Task<IEnumerable<Chunk>> DecodeAsync(Stream stream, string contentType, CancellationToken cancellationToken = default);
}

public record class Chunk(int PageNumber, int IndexOnPage, string Content);