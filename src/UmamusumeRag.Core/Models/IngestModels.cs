namespace UmamusumeRag.Core.Models;

public enum IngestStatus
{
    /// <summary>The file was loaded, chunked, embedded, and stored.</summary>
    Ingested,

    /// <summary>The file was already ingested and its content has not changed.</summary>
    Skipped,

    /// <summary>The file could not be ingested; see <see cref="IngestResult.Message"/>.</summary>
    Failed,
}

/// <summary>Outcome of ingesting one file.</summary>
public record IngestResult(string SourcePath, IngestStatus Status, int ChunkCount, string? Message = null);
