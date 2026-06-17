using System.ClientModel;
using System.Net.Mime;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using OpenAI;
using OpenAI.Responses;
using SqlDatabaseVectorSearch.Components;
using SqlDatabaseVectorSearch.ContentDecoders;
using SqlDatabaseVectorSearch.Data;
using SqlDatabaseVectorSearch.Extensions;
using SqlDatabaseVectorSearch.Services;
using SqlDatabaseVectorSearch.Settings;
using SqlDatabaseVectorSearch.TextChunkers;
using SqlDatabaseVectorSearch.Workflows;
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

builder.Services.AddSqlServer<ApplicationDbContext>(builder.Configuration.GetConnectionString("SqlConnection"), optionsAction: options =>
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

builder.Services.ConfigureHttpClientDefaults(configure =>
{
    configure.AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(2);
    });
});

builder.Services.AddSingleton(_ =>
{
    var embeddingClient = new OpenAIClient(new ApiKeyCredential(aiSettings.Embedding.ApiKey), new()
    {
        Endpoint = new(aiSettings.Embedding.Endpoint),
    }).GetEmbeddingClient(aiSettings.Embedding.Deployment).AsIEmbeddingGenerator(aiSettings.Embedding.Dimensions);

    return embeddingClient;
});

builder.Services.AddChatClient(_ =>
{
    var chatClient = new OpenAIClient(new ApiKeyCredential(aiSettings.ChatCompletion.ApiKey), new()
    {
        Endpoint = new(aiSettings.ChatCompletion.Endpoint),
    }).GetResponsesClient().AsIChatClientWithStoredOutputDisabled(aiSettings.ChatCompletion.Deployment);

    return chatClient;
});

// Semantic Kernel is used to generate embeddings and to reformulate questions taking into account all the previous interactions,
// so that embeddings themselves can be generated more accurately.
builder.Services.AddKernel()
    .AddAzureOpenAIChatCompletion(aiSettings.ChatCompletion.Deployment, aiSettings.ChatCompletion.Endpoint, aiSettings.ChatCompletion.ApiKey, modelId: aiSettings.ChatCompletion.ModelId);

builder.Services.AddKeyedSingleton<IContentDecoder, PdfContentDecoder>(MediaTypeNames.Application.Pdf);
builder.Services.AddKeyedSingleton<IContentDecoder, DocxContentDecoder>("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
builder.Services.AddKeyedSingleton<IContentDecoder, TextContentDecoder>(MediaTypeNames.Text.Plain);
builder.Services.AddKeyedSingleton<IContentDecoder, TextContentDecoder>(MediaTypeNames.Text.Markdown);

builder.Services.AddKeyedSingleton<ITextChunker, DefaultTextChunker>(KeyedService.AnyKey);
builder.Services.AddKeyedSingleton<ITextChunker, MarkdownTextChunker>(MediaTypeNames.Text.Markdown);

builder.Services.AddSingleton<TokenizerService>();
builder.Services.AddSingleton<ChatService>();

builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<VectorSearchService>();

builder.Services.AddSingleton<FormFileToEmbeddingRequestExecutor>();
builder.Services.AddSingleton<GenerateEmbeddingExecutor>();
builder.Services.AddScoped<StoreEmbeddingExecutor>();   // This executor is registered as scoped because it uses the DbContext, which is also scoped.

builder.AddWorkflow("EmbeddingWorkflow", (services, key) =>
{
    var formfileToConversionRequestExecutor = services.GetRequiredService<FormFileToEmbeddingRequestExecutor>();
    var generateEmbeddingExecutor = services.GetRequiredService<GenerateEmbeddingExecutor>();
    var storeEmbeddingExecutor = services.GetRequiredService<StoreEmbeddingExecutor>();

    var workflow = new WorkflowBuilder(formfileToConversionRequestExecutor).WithName(key)
        .AddEdge(formfileToConversionRequestExecutor, generateEmbeddingExecutor)
        .AddEdge(generateEmbeddingExecutor, storeEmbeddingExecutor)
        .WithOutputFrom(storeEmbeddingExecutor)
        .Build(validateOrphans: true);

    return workflow;
}, ServiceLifetime.Scoped);

builder.Services.AddOpenApi(options =>
{
    options.RemoveServerList();
    options.AddDefaultProblemDetailsResponse();
});

ValidatorOptions.Global.LanguageManager.Enabled = false;
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddDefaultProblemDetails();
builder.Services.AddDefaultExceptionHandler();

var app = builder.Build();
await ConfigureDatabaseAsync(app.Services);

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.UseWhen(context => context.IsWebRequest(), builder =>
{
    if (!app.Environment.IsDevelopment())
    {
        builder.UseExceptionHandler("/error", createScopeForErrors: true);

        // The default HSTS value is 30 days.
        builder.UseHsts();
    }

    builder.UseStatusCodePagesWithRedirects("/error?code={0}");
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
app.UseRequestLocalization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapEndpoints();

app.Run();

static async Task ConfigureDatabaseAsync(IServiceProvider serviceProvider)
{
    await using var scope = serviceProvider.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    await dbContext.Database.MigrateAsync();
}