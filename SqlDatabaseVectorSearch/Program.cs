using System.ComponentModel;
using System.Net.Mime;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using MimeMapping;
using SqlDatabaseVectorSearch.Components;
using SqlDatabaseVectorSearch.ContentDecoders;
using SqlDatabaseVectorSearch.DataAccessLayer;
using SqlDatabaseVectorSearch.Extensions;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Services;
using SqlDatabaseVectorSearch.Settings;
using SqlDatabaseVectorSearch.TextChunkers;
using TinyHelpers.AspNetCore.Extensions;
using TinyHelpers.AspNetCore.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// Add services to the container.
var aiSettings = builder.Services.ConfigureAndGet<AzureOpenAISettings>(builder.Configuration, "AzureOpenAI")!;
var appSettings = builder.Services.ConfigureAndGet<AppSettings>(builder.Configuration, nameof(AppSettings))!;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddBlazorBootstrap();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddAzureSql<ApplicationDbContext>(builder.Configuration.GetConnectionString("SqlConnection"), options =>
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

builder.Services.ConfigureHttpClientDefaults(builder =>
{
    builder.AddStandardResilienceHandler();
});

// Semantic Kernel is used to generate embeddings and to reformulate questions taking into account all the previous interactions,
// so that embeddings themselves can be generated more accurately.
builder.Services.AddKernel()
    .AddAzureOpenAITextEmbeddingGeneration(aiSettings.Embedding.Deployment, aiSettings.Embedding.Endpoint, aiSettings.Embedding.ApiKey, dimensions: aiSettings.Embedding.Dimensions)
    .AddAzureOpenAIChatCompletion(aiSettings.ChatCompletion.Deployment, aiSettings.ChatCompletion.Endpoint, aiSettings.ChatCompletion.ApiKey);

builder.Services.AddSingleton<TokenizerService>();
builder.Services.AddSingleton<ChatService>();

builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<VectorSearchService>();

builder.Services.AddKeyedSingleton<IContentDecoder, PdfContentDecoder>(MediaTypeNames.Application.Pdf);
builder.Services.AddKeyedSingleton<IContentDecoder, DocxContentDecoder>("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
builder.Services.AddKeyedSingleton<IContentDecoder, TextContentDecoder>(MediaTypeNames.Text.Plain);
builder.Services.AddKeyedSingleton<IContentDecoder, TextContentDecoder>(MediaTypeNames.Text.Markdown);

builder.Services.AddKeyedSingleton<ITextChunker, DefaultTextChunker>(KeyedService.AnyKey);
builder.Services.AddKeyedSingleton<ITextChunker, MarkdownTextChunker>(MediaTypeNames.Text.Markdown);

builder.Services.AddOpenApi(options =>
{
    options.RemoveServerList();
    options.AddDefaultProblemDetailsResponse();
});

builder.Services.AddDefaultProblemDetails();
builder.Services.AddDefaultExceptionHandler();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.UseWhen(context => context.IsWebRequest(), builder =>
{
    if (!app.Environment.IsDevelopment())
    {
        builder.UseExceptionHandler("/error");

        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        builder.UseHsts();
    }

    builder.UseStatusCodePagesWithReExecute("/error");
});

app.UseWhen(context => context.IsApiRequest(), builder =>
{
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        StatusCodeSelector = exception => exception switch
        {
            NotSupportedException => StatusCodes.Status501NotImplemented,
            _ => StatusCodes.Status500InternalServerError
        }
    });

    builder.UseStatusCodePages();
});

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", builder.Environment.ApplicationName);
});

app.UseRouting();
// app.UseRateLimiter();
app.UseRequestLocalization();
// app.UseCors();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/api/ask", async (Question question, VectorSearchService vectorSearchService, CancellationToken cancellationToken,
    [Description("If true, the question will be reformulated taking into account the context of the chat identified by the given ConversationId.")] bool reformulate = true) =>
{
    var response = await vectorSearchService.AskQuestionAsync(question, reformulate, cancellationToken);
    return TypedResults.Ok(response);
})
.WithSummary("Asks a question")
.WithDescription("The question will be reformulated taking into account the context of the chat identified by the given ConversationId.")
.WithTags("Ask");

app.MapPost("/api/ask-streaming", (Question question, VectorSearchService vectorSearchService, CancellationToken cancellationToken,
    [Description("If true, the question will be reformulated taking into account the context of the chat identified by the given ConversationId.")] bool reformulate = true) =>
{
    async IAsyncEnumerable<QuestionResponse> Stream()
    {
        // Requests a streaming response.
        var responseStream = vectorSearchService.AskStreamingAsync(question, reformulate, cancellationToken);

        await foreach (var delta in responseStream)
        {
            yield return delta;
        }
    }

    return Stream();
})
.WithSummary("Asks a question and gets the response as streaming")
.WithDescription("The question will be reformulated taking into account the context of the chat identified by the given ConversationId.")
.WithTags("Ask");

var documentsApiGroup = app.MapGroup("/api/documents").WithTags("Documents");

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

app.Run();