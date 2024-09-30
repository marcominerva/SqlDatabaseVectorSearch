using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel;
using MinimalHelpers.OpenApi;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Services;
using SqlDatabaseVectorSearch.Settings;
using TinyHelpers.AspNetCore.Extensions;
using TinyHelpers.AspNetCore.Swagger;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// Add services to the container.
var aiSettings = builder.Configuration.GetSection<AzureOpenAISettings>("AzureOpenAI")!;
var appSettings = builder.Services.ConfigureAndGet<AppSettings>(builder.Configuration, nameof(AppSettings))!;

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped(_ =>
{
    var sqlConnection = new SqlConnection(builder.Configuration.GetConnectionString("SqlConnection"));
    return sqlConnection;
});

builder.Services.AddMemoryCache();

// Semantic Kernel is used to generate embeddings and to reformulate questions taking into account all the previous interactions,
// so that embeddings themselves can be generated more accurately.
builder.Services.AddKernel()
    .AddAzureOpenAITextEmbeddingGeneration(aiSettings.Embedding.Deployment, aiSettings.Embedding.Endpoint, aiSettings.Embedding.ApiKey, dimensions: aiSettings.Embedding.Dimensions)
    .AddAzureOpenAIChatCompletion(aiSettings.ChatCompletion.Deployment, aiSettings.ChatCompletion.Endpoint, aiSettings.ChatCompletion.ApiKey);

builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<VectorSearchService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "SQL Database Vector Search API", Version = "v1" });

    options.AddDefaultResponse();
});

builder.Services.AddDefaultProblemDetails();
builder.Services.AddDefaultExceptionHandler();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = string.Empty;
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SQL Database Vector Search API v1");
    });
}

var documentsApiGroup = app.MapGroup("/api/documents").WithTags("Documents");

documentsApiGroup.MapGet(string.Empty, async (VectorSearchService vectorSearchService) =>
{
    var documents = await vectorSearchService.GetDocumentsAsync();
    return TypedResults.Ok(documents);
})
.WithOpenApi(operation =>
{
    operation.Summary = "Gets the list of documents";
    return operation;
});

documentsApiGroup.MapGet("{documentId:guid}/chunks", async (Guid documentId, VectorSearchService vectorSearchService) =>
{
    var documents = await vectorSearchService.GetDocumentChunksAsync(documentId);
    return TypedResults.Ok(documents);
})
.WithOpenApi(operation =>
{
    operation.Summary = "Gets the list of chunks of a given document";
    operation.Description = "The list does not contain embedding. Use '/api/documents/{documentId}/chunks/{documentChunkId}' to get the embedding for a given chunk.";

    return operation;
});

documentsApiGroup.MapGet("{documentId:guid}/chunks/{documentChunkId:guid}", async Task<Results<Ok<DocumentChunk>, NotFound>> (Guid documentId, Guid documentChunkId, VectorSearchService vectorSearchService) =>
{
    var chunk = await vectorSearchService.GetDocumentChunkEmbeddingAsync(documentId, documentChunkId);
    if (chunk is null)
    {
        return TypedResults.NotFound();
    }

    return TypedResults.Ok(chunk);
})
.WithOpenApi(operation =>
{
    operation.Summary = "Gets the details of a given chunk, includings its embedding";
    return operation;
});

documentsApiGroup.MapPost(string.Empty, async (IFormFile file, VectorSearchService vectorSearchService, Guid? documentId = null) =>
{
    using var stream = file.OpenReadStream();
    documentId = await vectorSearchService.ImportAsync(stream, file.FileName, documentId);

    return TypedResults.Ok(new UploadDocumentResponse(documentId.Value));
})
.DisableAntiforgery()
.WithOpenApi(operation =>
{
    operation.Summary = "Uploads a document";
    operation.Description = "Uploads a document to SQL Database and saves its embedding using Vector Support. The document will be indexed and used to answer questions. Currently, only PDF files are supported.";

    operation.Parameter("documentId").Description = "The unique identifier of the document. If not provided, a new one will be generated. If you specify an existing documentId, the corresponding document will be overwritten.";

    return operation;
});

documentsApiGroup.MapDelete("{documentId:guid}", async (Guid documentId, VectorSearchService vectorSearchService) =>
{
    await vectorSearchService.DeleteDocumentAsync(documentId);
    return TypedResults.NoContent();
})
.WithOpenApi(operation =>
{
    operation.Summary = "Deletes a document";
    operation.Description = "This endpoint deletes the document and all its chunks.";

    return operation;
});

app.MapPost("/api/ask", async (Question question, VectorSearchService vectorSearchService, bool reformulate = true) =>
{
    var response = await vectorSearchService.AskQuestionAsync(question, reformulate);
    return TypedResults.Ok(response);
})
.WithOpenApi(operation =>
{
    operation.Summary = "Asks a question";
    operation.Description = "The question will be reformulated taking into account the context of the chat identified by the given ConversationId.";

    operation.Parameter("reformulate").Description = "If true, the question will be reformulated taking into account the context of the chat identified by the given ConversationId.";

    return operation;
})
.WithTags("Ask");

app.Run();