using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SqlDatabaseVectorSearch.ContentDecoders;
using SqlDatabaseVectorSearch.Services;
using SqlDatabaseVectorSearch.Settings;

namespace SqlDatabaseVectorSearch.Workflows;

public partial class GenerateEmbeddingExecutor(IServiceProvider serviceProvider, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator, TokenizerService tokenizerService, IOptions<AppSettings> appSettingsOptions, ILogger<GenerateEmbeddingExecutor> logger) : Executor(nameof(GenerateEmbeddingExecutor))
{
    private readonly AppSettings appSettings = appSettingsOptions.Value;

    [MessageHandler]
    private async ValueTask<EmbeddingResponse> HandleAsync(EmbeddingRequest request, IWorkflowContext context, CancellationToken cancellationToken)
    {
        // Extract the contents of the file.
        var decoder = serviceProvider.GetKeyedService<IContentDecoder>(request.ContentType) ?? throw new NotSupportedException($"Content type '{request.ContentType}' is not supported.");
        var chunks = await decoder.DecodeAsync(request.Content, request.ContentType, cancellationToken);
        var chunkContents = chunks.Select(p => p.Content).ToList();

        // We get the token count of the whole document because it is the total number of token used by embedding (it may be necessary, for example, for cost analysis).
        var tokenCount = tokenizerService.CountEmbeddingTokens(string.Join(" ", chunkContents));

        // Process paragraphs in batches.
        var embeddings = new List<Embedding<float>>();
        foreach (var batch in chunkContents.Chunk(appSettings.EmbeddingBatchSize))
        {
            logger.LogDebug("Processing batch of {Count} chunks for embedding generation...", batch.Length);

            // Generate embeddings for this batch.
            var batchEmbeddings = await embeddingGenerator.GenerateAsync(batch, cancellationToken: cancellationToken);
            embeddings.AddRange(batchEmbeddings);
        }

        return new EmbeddingResponse(request, chunks, embeddings, tokenCount);
    }
}

public record class EmbeddingResponse(EmbeddingRequest Request, IEnumerable<Chunk> Chunks, IEnumerable<Embedding<float>> Embeddings, int TokenCount);