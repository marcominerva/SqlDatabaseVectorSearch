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

// Semantical Kernel is used to reformulate questions taking into account all the previous interactions, so that embeddings can be generate more accurately.
builder.Services.AddKernel()
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

var documentsApiGroup = app.MapGroup("/api/documents");

documentsApiGroup.MapPost(string.Empty, async (IFormFile file, VectorSearchService vectorSearchService, LinkGenerator linkGenerator, Guid? documentId = null) =>
{
    documentId = await vectorSearchService.ImportAsync(file.OpenReadStream(), file.FileName, documentId);
    return TypedResults.Ok(new UploadDocumentResponse(documentId.Value));
})
.DisableAntiforgery()
.WithOpenApi(operation =>
{
    operation.Summary = "Uploads a document. Currently, only PDF files are supported";
    operation.Description = "Uploads a document to SQL Server. The document will be indexed and used to answer questions. The documentId is optional, if not provided a new one will be generated. If you specify an existing documentId, the document will be overridden.";

    operation.Parameter("documentId").Description = "The unique identifier of the document. If not provided, a new one will be generated. If you specify an existing documentId, the document will be overridden.";

    return operation;
})
;

documentsApiGroup.MapDelete("{documentId:guid}", async (Guid documentId, VectorSearchService vectorSearchService) =>
{
    await vectorSearchService.DeleteDocumentAsync(documentId);
    return TypedResults.NoContent();
})
.WithOpenApi(operation =>
{
    operation.Summary = "Delete a document from SQL Server";

    return operation;
});

//app.MapPost("/api/search", async (Search search, ApplicationMemoryService memory, double minimumRelevance = 0, string? index = null) =>
//{
//    var response = await memory.SearchAsync(search, minimumRelevance, index);
//    return TypedResults.Ok(response);
//})
//.WithOpenApi(operation =>
//{
//    operation.Summary = "Search into Kernel Memory";
//    operation.Description = "Search into Kernel Memory using the provided question and optional tags. If tags are provided, they will be used as filters with OR logic.";

//    operation.Parameter("minimumRelevance").Description = "The minimum Cosine Similarity required.";
//    operation.Parameter("index").Description = "The index in which to search for documents. If not provided, the default index will be used ('default').";

//    return operation;
//});

//app.MapPost("/api/ask", async Task<Results<Ok<MemoryResponse>, NotFound>> (Question question, ApplicationMemoryService memory, bool reformulate = true, double minimumRelevance = 0, string? index = null) =>
//{
//    var response = await memory.AskQuestionAsync(question, reformulate, minimumRelevance, index);
//    if (response is null)
//    {
//        return TypedResults.NotFound();
//    }

//    return TypedResults.Ok(response);
//})
//.WithOpenApi(operation =>
//{
//    operation.Summary = "Ask a question to the Kernel Memory Service";
//    operation.Description = "Ask a question to the Kernel Memory Service using the provided question and optional tags. The question will be reformulated taking into account the context of the chat identified by the given ConversationId. If tags are provided, they will be used as filters with OR logic.";

//    operation.Parameter("reformulate").Description = "If true, the question will be reformulated taking into account the context of the chat identified by the given ConversationId.";
//    operation.Parameter("minimumRelevance").Description = "The minimum Cosine Similarity required.";
//    operation.Parameter("index").Description = "The index in which to search for documents. If not provided, the default index will be used ('default').";

//    return operation;
//});

app.Run();