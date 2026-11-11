using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Models;
using UmamusumeRag.Core.Services;

namespace UmamusumeRag.Api.Endpoints;

// ===== RAG write path · HTTP entry points ==================================
// These endpoints are thin: they resolve the folder/file and delegate to
// DocumentIngestionService (steps 1-4). All RAG logic lives in Core.
//
// Request flow:
//   POST /api/documents/ingest  -> IngestAsync  -> DocumentIngestionService.IngestDirectoryAsync
//   POST /api/documents/upload  -> UploadAsync  -> DocumentIngestionService.IngestFileAsync
//
// TWEAK: upload size limits, where uploads are stored, whether ingest runs in
//        the background (return 202 + a job id) instead of inline.
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
        DocumentIngestionService ingestion,
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

        var results = await ingestion.IngestDirectoryAsync(directory, force, cancellationToken);
        return Results.Ok(results);
    }

    /// <summary>
    /// RAG steps 1-4 for a single uploaded file (saved under data/uploads first).
    /// force=true because an upload usually means "ingest this now".
    /// </summary>
    private static async Task<IResult> UploadAsync(
        IFormFile file,
        DocumentIngestionService ingestion,
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

        var result = await ingestion.IngestFileAsync(targetPath, force: true, cancellationToken);
        return result.Status == IngestStatus.Failed
            ? Results.Problem(result.Message)
            : Results.Ok(result);
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
