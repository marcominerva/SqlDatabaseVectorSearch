using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel;
using MinimalHelpers.OpenApi;
using SqlDatabaseVectorSearch.DataAccessLayer;
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

builder.Services.AddSqlServer<ApplicationDbContext>(builder.Configuration.GetConnectionString("SqlConnection"), options =>
{
    options.EnableRetryOnFailure(3, TimeSpan.FromSeconds(1), null);
    options.UseVectorSearch();
});

builder.Services.AddMemoryCache();

// Semantical Kernel is used to generate embeddings and to reformulate questions taking into account all the previous interactions,
// so that embeddings themselves can be generated more accurately.
builder.Services.AddKernel()
    .AddAzureOpenAITextEmbeddingGeneration(aiSettings.Embedding.Deployment, aiSettings.Embedding.Endpoint, aiSettings.Embedding.ApiKey)
    .AddAzureOpenAIChatCompletion(aiSettings.ChatCompletion.Deployment, aiSettings.ChatCompletion.Endpoint, aiSettings.ChatCompletion.ApiKey);

builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<VectorSearchService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "SQL Server Vector Search API", Version = "v1" });

    options.AddDefaultResponse();
    options.AddFormFile();
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
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Kernel Memory Service API v1");
        options.InjectStylesheet("/css/swagger.css");
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

documentsApiGroup.MapPost(string.Empty, async (IFormFile file, VectorSearchService vectorSearchService, LinkGenerator linkGenerator, Guid? documentId = null) =>
{
    using var stream = file.OpenReadStream();
    documentId = await vectorSearchService.ImportAsync(stream, file.FileName, documentId);

    return TypedResults.Ok(new UploadDocumentResponse(documentId.Value));
})
.DisableAntiforgery()
.WithOpenApi(operation =>
{
    operation.Summary = "Uploads a document. Currently, only PDF files are supported";
    operation.Description = "Uploads a document to SQL Server and saves its embeddings using Vector Support. The document will be indexed and used to answer questions.";

    operation.Parameter("documentId").Description = "The unique identifier of the document. If not provided, a new one will be generated. If you specify an existing documentId, the document will be overridden.";

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
    operation.Description = "This endpoint deletes the document and all its chunks from SQL Server.";

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