using Microsoft.Agents.AI.Workflows;
using SqlDatabaseVectorSearch.ContentDecoders;

namespace SqlDatabaseVectorSearch.Workflows;

public partial class ExtractChunksExecutor(IServiceProvider serviceProvider, ILogger<ExtractChunksExecutor> logger) : Executor(nameof(ExtractChunksExecutor))
{
    [MessageHandler]
    private async ValueTask<ExtractChunksResponse> HandleAsync(EmbeddingRequest request, IWorkflowContext context, CancellationToken cancellationToken)
    {
        // Extract the contents of the file.
        var decoder = serviceProvider.GetKeyedService<IContentDecoder>(request.ContentType) ?? throw new NotSupportedException($"Content type '{request.ContentType}' is not supported.");
        var chunks = await decoder.DecodeAsync(request.Content, request.ContentType, cancellationToken);

        logger.LogDebug("Extracted {Count} chunks from '{FileName}'.", chunks.Count(), request.FileName);

        return new(request, chunks);
    }
}

public record class ExtractChunksResponse(EmbeddingRequest Request, IEnumerable<Chunk> Chunks);
