using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Models;
using UmamusumeRag.Core.Services;

namespace UmamusumeRag.Api.Endpoints;

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

    private static async Task<IResult> UploadAsync(
        IFormFile file,
        DocumentIngestionService ingestion,
        RagOptions ragOptions,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return Results.BadRequest(new { error = "The uploaded file is empty." });

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
