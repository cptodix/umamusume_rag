using MassTransit;
using MassTransit.Mediator;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Messaging;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Api.Endpoints;

// ===== RAG write path · HTTP entry points ==================================
// Endpoints no longer call the pipeline services directly. They send a message
// through the in-process MassTransit mediator; a consumer in Infrastructure
// adapts it to DocumentIngestionService (RAG steps 1-4).
//
//   POST /api/documents/ingest -> IRequestClient<IngestDocuments> -> consumer
//   POST /api/documents/upload -> IRequestClient<IngestDocument>  -> consumer
//
// WHY: the HTTP layer stays free of pipeline logic, and the same messages can
// later be sent over a real transport (RabbitMQ, Azure Service Bus) without
// touching this file.
//
// TWEAK: upload size limits, where uploads are stored, or make ingest
//        asynchronous (return 202 + a job id) by publishing instead of requesting.
// ============================================================================

public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents").WithTags("Documents");

        group.MapPost("/ingest", IngestAsync)
            .WithName("IngestDocuments")
            .WithSummary("Scans a folder for supported files (.md, .txt, .docx) and ingests them: chunk -> embed -> store.");

        group.MapPost("/upload", UploadAsync)
            .WithName("UploadDocument")
            .WithSummary("Uploads a single file and ingests it.")
            .DisableAntiforgery();

        group.MapGet("/", ListAsync)
            .WithName("ListDocuments")
            .WithSummary("Lists all ingested documents.");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteDocument")
            .WithSummary("Deletes a document and all of its chunks.");

        return app;
    }

    /// <summary>
    /// RAG steps 1-4 for every supported file under a folder.
    /// Query: ?path=&lt;folder&gt; (defaults to Rag:DataPath), ?force=true (re-embed unchanged files).
    ///
    /// GOTCHA: files with no matching loader (e.g. .pdf today) are skipped
    /// silently — they do not appear in the response at all.
    /// </summary>
    private static async Task<IResult> IngestAsync(
        IScopedMediator mediator,
        RagOptions ragOptions,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken,
        string? path = null,
        bool force = false)
    {
        var directory = ResolveDataPath(path ?? ragOptions.DataPath, environment);

        if (!Directory.Exists(directory))
        {
            return Results.BadRequest(new
            {
                error = $"Directory '{directory}' does not exist. Create it, drop your documents in, and retry.",
            });
        }

        try
        {
            var client = mediator.CreateRequestClient<IngestDocuments>();
            var response = await client.GetResponse<IngestDocumentsResult>(
                new IngestDocuments { Directory = directory, Force = force },
                cancellationToken);

            return Results.Ok(response.Message.Results);
        }
        catch (RequestException ex)
        {
            // Consumer exceptions propagate back through the mediator.
            return Results.Problem(ex.Message);
        }
    }

    /// <summary>
    /// RAG steps 1-4 for a single uploaded file (saved under data/uploads first).
    /// force=true because an upload usually means "ingest this now".
    /// </summary>
    private static async Task<IResult> UploadAsync(
        IFormFile file,
        IScopedMediator mediator,
        RagOptions ragOptions,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return Results.BadRequest(new { error = "The uploaded file is empty." });

        // TWEAK: Path.GetFileName strips directories (path traversal), but the
        //        name is not otherwise sanitized. Add size/type checks or a
        //        generated name if this ever faces untrusted users.
        var uploadsDirectory = Path.Combine(ResolveDataPath(ragOptions.DataPath, environment), "uploads");
        Directory.CreateDirectory(uploadsDirectory);

        var targetPath = Path.Combine(uploadsDirectory, Path.GetFileName(file.FileName));
        await using (var stream = File.Create(targetPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        try
        {
            var client = mediator.CreateRequestClient<IngestDocument>();
            var response = await client.GetResponse<IngestResult>(
                new IngestDocument { FilePath = targetPath, Force = true },
                cancellationToken);

            return response.Message.Status == IngestStatus.Failed
                ? Results.Problem(response.Message.Message)
                : Results.Ok(response.Message);
        }
        catch (RequestException ex)
        {
            return Results.Problem(ex.Message);
        }
    }

    private static async Task<IResult> ListAsync(IDocumentRepository repository, CancellationToken cancellationToken) =>
        Results.Ok(await repository.ListAsync(cancellationToken));

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IDocumentRepository repository,
        CancellationToken cancellationToken) =>
        await repository.RemoveAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    /// <summary>Resolves relative paths (like the default "data") against the app content root.</summary>
    private static string ResolveDataPath(string configuredPath, IWebHostEnvironment environment) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
}
