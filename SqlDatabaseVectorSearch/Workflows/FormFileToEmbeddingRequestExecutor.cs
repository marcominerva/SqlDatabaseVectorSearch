using Microsoft.Agents.AI.Workflows;

namespace SqlDatabaseVectorSearch.Workflows;

public partial class FormFileToEmbeddingRequestExecutor() : Executor(nameof(FormFileToEmbeddingRequestExecutor))
{
    [MessageHandler]
    private ValueTask<EmbeddingRequest> HandleAsync(FormFileEmbeddingRequest request, IWorkflowContext context, CancellationToken cancellationToken)
    {
        // Note: file.ContentType is not 100% reliable (for example, for markdown file).
        var embeddingRequest = new EmbeddingRequest(request.File.OpenReadStream(), Path.GetFileName(request.File.FileName), MimeMapping.MimeUtility.GetMimeMapping(request.File.FileName), request.DocumentId);
        return ValueTask.FromResult(embeddingRequest);
    }
}

public record class FormFileEmbeddingRequest(IFormFile File, Guid? DocumentId);

public record class EmbeddingRequest(Stream Content, string FileName, string ContentType, Guid? DocumentId);