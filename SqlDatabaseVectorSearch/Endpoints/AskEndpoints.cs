using System.ComponentModel;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using MinimalHelpers.FluentValidation;
using SqlDatabaseVectorSearch.Models;
using SqlDatabaseVectorSearch.Services;

namespace SqlDatabaseVectorSearch.Endpoints;

public class AskEndpoints : IEndpointRouteHandlerBuilder
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/ask", async (Question question, VectorSearchService vectorSearchService, CancellationToken cancellationToken,
            [Description("If true, the question will be reformulated taking into account the context of the chat identified by the given ConversationId.")] bool reformulate = true) =>
        {
            var response = await vectorSearchService.AskQuestionAsync(question, reformulate, cancellationToken);
            return TypedResults.Ok(response);
        })
        .WithValidation<Question>()
        .WithSummary("Asks a question")
        .WithDescription("The question will be reformulated taking into account the context of the chat identified by the given ConversationId.")
        .WithTags("Ask");

        endpoints.MapPost("/api/ask-streaming", (Question question, VectorSearchService vectorSearchService, CancellationToken cancellationToken,
            [Description("If true, the question will be reformulated taking into account the context of the chat identified by the given ConversationId.")] bool reformulate = true) =>
        {
            async IAsyncEnumerable<SseItem<Response>> StreamAsync([EnumeratorCancellation] CancellationToken innerCancellationToken)
            {
                // Requests a streaming response.
                var responseStream = vectorSearchService.AskStreamingAsync(question, reformulate, innerCancellationToken);

                await foreach (var delta in responseStream)
                {
                    yield return new(delta, delta.StreamState?.ToString());
                }
            }

            return TypedResults.ServerSentEvents(StreamAsync(cancellationToken));
        })
        .WithValidation<Question>()
        .WithSummary("Asks a question and gets the response as streaming")
        .WithDescription("The question will be reformulated taking into account the context of the chat identified by the given ConversationId.")
        .WithTags("Ask");
    }
}
