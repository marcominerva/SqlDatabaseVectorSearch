
using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using MimeMapping;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Services;

namespace SqlDatabaseVectorSearch.Endpoints;

public class DocumentEndpoints : IEndpointRouteHandlerBuilder
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var documentsApiGroup = endpoints.MapGroup("/api/documents").WithTags("Documents");

        documentsApiGroup.MapGet(string.Empty, async (DocumentService documentService, CancellationToken cancellationToken) =>
        {
            var documents = await documentService.GetAsync(cancellationToken);
            return TypedResults.Ok(documents);
        })
        .WithSummary("Gets the list of documents");

        documentsApiGroup.MapPost(string.Empty, async (IFormFile file, VectorSearchService vectorSearchService, CancellationToken cancellationToken,
            [Description("The unique identifier of the document. If not provided, a new one will be generated. If you specify an existing documentId, the corresponding document will be overwritten.")] Guid? documentId = null) =>
        {
            using var stream = file.OpenReadStream();

            // Note: file.ContentType is not 100% reliable (for example, for markdown file).
            var response = await vectorSearchService.ImportAsync(stream, file.FileName, MimeUtility.GetMimeMapping(file.FileName), documentId, cancellationToken);

            return TypedResults.Ok(response);
        })
        .DisableAntiforgery()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithSummary("Uploads a document")
        .WithDescription("Uploads a document to SQL Database and saves its embedding using the native VECTOR type. The document will be indexed and used to answer questions. Currently, PDF, DOCX, TXT and MD files are supported.");

        documentsApiGroup.MapGet("{documentId:guid}/chunks", async (Guid documentId, DocumentService documentService, CancellationToken cancellationToken) =>
        {
            var documents = await documentService.GetChunksAsync(documentId, cancellationToken);
            return TypedResults.Ok(documents);
        })
        .WithSummary("Gets the list of chunks of a given document")
        .WithDescription("The list does not contain embedding. Use '/api/documents/{documentId}/chunks/{documentChunkId}' to get the embedding for a given chunk.");

        documentsApiGroup.MapGet("{documentId:guid}/chunks/{documentChunkId:guid}", async Task<Results<Ok<DocumentChunk>, NotFound>> (Guid documentId, Guid documentChunkId, DocumentService documentService, CancellationToken cancellationToken) =>
        {
            var chunk = await documentService.GetChunkEmbeddingAsync(documentId, documentChunkId, cancellationToken);
            if (chunk is null)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(chunk);
        })
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Gets the details of a given chunk, includings its embedding");

        documentsApiGroup.MapDelete("{documentId:guid}", async (Guid documentId, DocumentService documentService, CancellationToken cancellationToken) =>
        {
            await documentService.DeleteAsync(documentId, cancellationToken);
            return TypedResults.NoContent();
        })
        .WithSummary("Deletes a document")
        .WithDescription("This endpoint deletes the document and all its chunks.");
    }
}
