using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using SqlDatabaseVectorSearch.DataAccessLayer;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Services;
using SqlDatabaseVectorSearch.Settings;
using TinyHelpers.AspNetCore.Extensions;
using TinyHelpers.AspNetCore.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// Add services to the container.
var aiSettings = builder.Services.ConfigureAndGet<AzureOpenAISettings>(builder.Configuration, "AzureOpenAI")!;
var appSettings = builder.Services.ConfigureAndGet<AppSettings>(builder.Configuration, nameof(AppSettings))!;

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSqlServer<ApplicationDbContext>(builder.Configuration.GetConnectionString("SqlConnection"), options =>
{
    options.UseVectorSearch();
}, options =>
{
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new()
    {
        LocalCacheExpiration = appSettings.MessageExpiration
    };
});

// Semantic Kernel is used to generate embeddings and to reformulate questions taking into account all the previous interactions,
// so that embeddings themselves can be generated more accurately.
builder.Services.AddKernel()
    .AddAzureOpenAITextEmbeddingGeneration(aiSettings.Embedding.Deployment, aiSettings.Embedding.Endpoint, aiSettings.Embedding.ApiKey, dimensions: aiSettings.Embedding.Dimensions)
    .AddAzureOpenAIChatCompletion(aiSettings.ChatCompletion.Deployment, aiSettings.ChatCompletion.Endpoint, aiSettings.ChatCompletion.ApiKey);

builder.Services.AddSingleton<TokenizerService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddScoped<VectorSearchService>();

builder.Services.AddOpenApi(options =>
{
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
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = string.Empty;
        options.SwaggerEndpoint("/openapi/v1.json", builder.Environment.ApplicationName);
    });
}

var documentsApiGroup = app.MapGroup("/api/documents").WithTags("Documents");

documentsApiGroup.MapGet(string.Empty, async (VectorSearchService vectorSearchService) =>
{
    var documents = await vectorSearchService.GetDocumentsAsync();
    return TypedResults.Ok(documents);
})
.WithSummary("Gets the list of documents");

documentsApiGroup.MapGet("{documentId:guid}/chunks", async (Guid documentId, VectorSearchService vectorSearchService) =>
{
    var documents = await vectorSearchService.GetDocumentChunksAsync(documentId);
    return TypedResults.Ok(documents);
})
.WithSummary("Gets the list of chunks of a given document")
.WithDescription("The list does not contain embedding. Use '/api/documents/{documentId}/chunks/{documentChunkId}' to get the embedding for a given chunk.");

documentsApiGroup.MapGet("{documentId:guid}/chunks/{documentChunkId:guid}", async Task<Results<Ok<DocumentChunk>, NotFound>> (Guid documentId, Guid documentChunkId, VectorSearchService vectorSearchService) =>
{
    var chunk = await vectorSearchService.GetDocumentChunkEmbeddingAsync(documentId, documentChunkId);
    if (chunk is null)
    {
        return TypedResults.NotFound();
    }

    return TypedResults.Ok(chunk);
})
.ProducesProblem(StatusCodes.Status404NotFound)
.WithSummary("Gets the details of a given chunk, includings its embedding");

documentsApiGroup.MapPost(string.Empty, async (IFormFile file, VectorSearchService vectorSearchService,
    [Description("The unique identifier of the document. If not provided, a new one will be generated. If you specify an existing documentId, the corresponding document will be overwritten.")] Guid? documentId = null) =>
{
    using var stream = file.OpenReadStream();
    documentId = await vectorSearchService.ImportAsync(stream, file.FileName, documentId);

    return TypedResults.Ok(new UploadDocumentResponse(documentId.Value));
})
.DisableAntiforgery()
.ProducesProblem(StatusCodes.Status400BadRequest)
.WithSummary("Uploads a document")
.WithDescription("Uploads a document to SQL Database and saves its embedding using the new native Vector type. The document will be indexed and used to answer questions. Currently, only PDF files are supported.");

documentsApiGroup.MapDelete("{documentId:guid}", async (Guid documentId, VectorSearchService vectorSearchService) =>
{
    await vectorSearchService.DeleteDocumentAsync(documentId);
    return TypedResults.NoContent();
})
.WithSummary("Deletes a document")
.WithDescription("This endpoint deletes the document and all its chunks.");

app.MapPost("/api/ask", async (Question question, VectorSearchService vectorSearchService,
    [Description("If true, the question will be reformulated taking into account the context of the chat identified by the given ConversationId.")] bool reformulate = true) =>
{
    var response = await vectorSearchService.AskQuestionAsync(question, reformulate);
    return TypedResults.Ok(response);
})
.WithSummary("Asks a question")
.WithDescription("The question will be reformulated taking into account the context of the chat identified by the given ConversationId.")
.WithTags("Ask");

app.Run();