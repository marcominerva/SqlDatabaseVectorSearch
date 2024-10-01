using System.Text.Json;

namespace SqlDatabaseVectorSearch.Models;

public record class DocumentChunk(Guid Id, int Index, string Content, float[]? Embedding)
{
    public DocumentChunk(Guid Id, int Index, string Content) : this(Id, Index, Content, (float[]?)null)
    {
    }

    public DocumentChunk(Guid Id, int Index, string Content, string Embedding) : this(Id, Index, Content, JsonSerializer.Deserialize<float[]?>(Embedding))
    {
    }
}
