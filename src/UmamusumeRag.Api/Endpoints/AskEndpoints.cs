using UmamusumeRag.Core.Models;
using UmamusumeRag.Core.Services;

namespace UmamusumeRag.Api.Endpoints;

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

        var response = await rag.AskAsync(request.Question, request.TopK, cancellationToken);
        return Results.Ok(response);
    }
}
