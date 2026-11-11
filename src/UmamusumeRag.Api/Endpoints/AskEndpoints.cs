using MassTransit;
using MassTransit.Mediator;
using UmamusumeRag.Core.Messaging;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Api.Endpoints;

// ===== RAG read path · HTTP entry point ====================================
// POST /api/ask sends an AskQuestion message through the in-process MassTransit
// mediator; AskQuestionConsumer adapts it to RagQueryService (RAG steps 5-8).
//
// Request:  { "question": "...", "topK": 5 }   (topK optional)
// Response: { "answer": "...", "sources": [ { index, documentTitle,
//             sourcePath, chunkIndex, snippet, score }, ... ] }
//
// The `sources` array is your primary debugging tool: if the right chunk is
// not in it, the model never saw it and the bug is in retrieval (steps 5-6),
// not in generation (steps 7-8).
//
// TWEAK: this is where to add streaming (SSE), chat history, or auth.
// ============================================================================

public static class AskEndpoints
{
    public static IEndpointRouteBuilder MapAskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("RAG");

        group.MapPost("/ask", AskAsync)
            .WithName("Ask")
            .WithSummary("Answers a question from the ingested documents (embed -> retrieve -> generate) and returns the sources used.");

        return app;
    }

    private static async Task<IResult> AskAsync(
        AskRequest request,
        IScopedMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return Results.BadRequest(new { error = "The 'question' field must not be empty." });

        try
        {
            var client = mediator.CreateRequestClient<AskQuestion>();
            var response = await client.GetResponse<AskResponse>(
                new AskQuestion { Question = request.Question, TopK = request.TopK },
                cancellationToken);

            return Results.Ok(response.Message);
        }
        catch (RequestException ex)
        {
            // Consumer exceptions (e.g. provider 401/timeout) propagate back here.
            return Results.Problem(ex.Message);
        }
    }
}
