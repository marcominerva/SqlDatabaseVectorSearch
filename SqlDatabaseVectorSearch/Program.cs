using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Mime;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
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
        Transport = new HttpClientPipelineTransport(new HttpClient(new TraceHttpClientHandler()))
    }).GetResponsesClient().AsIChatClientWithStoredOutputDisabled(aiSettings.ChatCompletion.Deployment);

    return chatClient;
});

builder.Services.AddKeyedSingleton<IContentDecoder, PdfContentDecoder>(MediaTypeNames.Application.Pdf);
builder.Services.AddKeyedSingleton<IContentDecoder, DocxContentDecoder>("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
builder.Services.AddKeyedSingleton<IContentDecoder, TextContentDecoder>(MediaTypeNames.Text.Plain);
builder.Services.AddKeyedSingleton<IContentDecoder, TextContentDecoder>(MediaTypeNames.Text.Markdown);

builder.Services.AddKeyedSingleton<ITextChunker, DefaultTextChunker>(KeyedService.AnyKey);
builder.Services.AddKeyedSingleton<ITextChunker, MarkdownTextChunker>(MediaTypeNames.Text.Markdown);

builder.Services.AddSingleton<TokenizerService>();

builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<VectorSearchService>();
builder.Services.AddScoped<ContextProvider>();

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

builder.Services.AddAIAgent("ReformulationAgent", (services, key) =>
{
    var chatClient = services.GetRequiredService<IChatClient>();

    return chatClient.AsAIAgent(new ChatClientAgentOptions()
    {
        Id = key.ToLowerInvariant(),
        Name = key,
        ChatOptions = new()
        {
            Instructions = """
                You are a helpful assistant that reformulates questions to perform embeddings search.
                Your task is to reformulate the question taking into account the context of the chat.
                The reformulated question must always explicitly contain the subject of the question.

                You MUST reformulate the question in the SAME language as the user's question.
                For example, if the user asks a question in English, the reformulated question MUST be in English. If the user asks in Italian, the reformulated question MUST be in Italian.

                Never add "in this chat", "in the context of this chat", "in the context of our conversation", "search for" or something like that in your answer.
                Your answer must contain only the reformulated question and nothing else.
                Never add follow-up messages, clarifications, notes, disclaimers, or requests for more information such as "if you give me more information, I can be more precise".
                """,
            Reasoning = new()
            {
                Effort = ReasoningEffort.None,
                Output = ReasoningOutput.None
            }
        },
        ChatHistoryProvider = new InMemoryChatHistoryProvider(new()
        {
            StorageInputRequestMessageFilter = _ => [],
            StorageInputResponseMessageFilter = _ => []
        })
    },
    loggerFactory: services.GetRequiredService<ILoggerFactory>(),
    services: services);
});

var textSearchOptions = new TextSearchProviderOptions()
{
    ContextFormatter = results =>
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Additional Context");
        sb.AppendLine("Use the excerpts below to answer the user.");
        sb.AppendLine("Citation rules:");
        sb.AppendLine("- Do NOT add inline citations.");
        sb.AppendLine("- At the END of your answer, add a sources section that follows this template exactly, where the sources label and the page label are localized in the same language as the user's question:");
        sb.AppendLine("  *localized-sources-label*");
        sb.AppendLine("  1. **SourceName**, localized-page-label PageNumber: *supporting excerpt of about 15-20 words*");
        sb.AppendLine("- Omit the page label and the page number when the page number is not available.");
        sb.AppendLine("- Do NOT use headings or links in the sources section.");
        sb.AppendLine("- Include ONLY sources you actually used. No duplicates.");
        sb.AppendLine();

        sb.AppendLine("### Sources");
        foreach (var (i, r) in results.Index())
        {
            sb.AppendLine($"[{i + 1}] {GetSourceName(r, i)}");
            sb.AppendLine(r.Text);
            sb.AppendLine("---");
        }

        return sb.ToString();

        static string GetSourceName(TextSearchProvider.TextSearchResult result, int index)
        {
            var name = string.IsNullOrWhiteSpace(result.SourceName) ? $"Source {index + 1}" : result.SourceName;
            var pageNumber = result.RawRepresentation is int number ? number : (int?)null;
            var pageText = pageNumber.HasValue ? $", page {pageNumber}" : string.Empty;

            return $"{name}{pageText}";
        }
    }
};

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new()
    {
        LocalCacheExpiration = appSettings.MessageExpiration
    };
});
builder.Services.AddSingleton<HybridCacheSessionStoreService>();

builder.Services.AddAIAgent("RagAgent", (services, key) =>
{
    var chatClient = services.GetRequiredService<IChatClient>();

    return chatClient.AsAIAgent(new ChatClientAgentOptions
    {
        Id = key.ToLowerInvariant(),
        Name = key,
        ChatOptions = new()
        {
            Instructions = """
                You are a helpful assistant. Answer questions using the provided context and cite the source document when available.
                You can use only the information provided in this chat to answer questions. If you don't know the answer, reply suggesting to refine the question.

                For example, if the user asks "What is the capital of Italy?" and in this chat there isn't information about Italy, you should reply something like:
                - This information isn't available in the given context.
                - I'm sorry, I don't know the answer to that question.
                - I don't have that information.
                - I don't know.
                - Given the context, I can't answer that question.
                - I'm sorry, I don't have enough information to answer that question.

                Never answer questions that are not related to this chat.
                """,
            Reasoning = new()
            {
                Effort = ReasoningEffort.Low,
                Output = ReasoningOutput.None
            }
        },
        ChatHistoryProvider = new InMemoryChatHistoryProvider(new()
        {
            ChatReducer = new MessageCountingChatReducer(appSettings.MessageLimit),
            ReducerTriggerEvent = InMemoryChatHistoryProviderOptions.ChatReducerTriggerEvent.AfterMessageAdded,
            StorageInputRequestMessageFilter = messages =>
            {
                return messages.Where(m => m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.ChatHistory
                    && m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.AIContextProvider);
            }
        }),
        AIContextProviders = [new TextSearchProvider(services.GetRequiredService<ContextProvider>().SearchAsync, textSearchOptions)]
    },
    loggerFactory: services.GetRequiredService<ILoggerFactory>(),
    services: services);
}, ServiceLifetime.Scoped)
.WithSessionStore((services, _) =>
{
    var sessionStore = services.GetRequiredService<HybridCacheSessionStoreService>();
    return sessionStore;
}, withIsolation: false);

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
        SuppressDiagnosticsCallback = _ => false,
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

public class TraceHttpClientHandler : HttpClientHandler
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestString = request.Content is null ? "(no request body)" : await request.Content.ReadAsStringAsync(cancellationToken);

        PrintText($"Raw Request ({request.RequestUri})", ConsoleColor.Green);
        PrintText(FormatJson(requestString), ConsoleColor.DarkGray);
        PrintSeparator();

        var response = await base.SendAsync(request, cancellationToken);

        return response;

        static void PrintText(string message, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        static void PrintSeparator() => Console.WriteLine(new string('-', 50));
    }

    private static string FormatJson(string input)
    {
        try
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(input);
            return JsonSerializer.Serialize(jsonElement, jsonSerializerOptions);
        }
        catch
        {
            return input;
        }
    }
}