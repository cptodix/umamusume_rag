using UmamusumeRag.Core.Models;
using UmamusumeRag.Core.Services;

namespace UmamusumeRag.Api.Endpoints;

// ===== RAG read path · HTTP entry point ====================================
// POST /api/ask -> RagQueryService.AskAsync (steps 5-8).
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
        RagQueryService rag,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return Results.BadRequest(new { error = "The 'question' field must not be empty." });

        // TWEAK: per-request TopK override — handy for experiments without
        //        restarting the app or editing appsettings.
        var response = await rag.AskAsync(request.Question, request.TopK, cancellationToken);
        return Results.Ok(response);
    }
}
