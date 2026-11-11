namespace UmamusumeRag.Core.Models;

// ===== RAG steps 1-4 · ingestion result ===================================
// One IngestResult per input file, returned by POST /api/documents/ingest and
// /upload. Failures are per-file on purpose: a batch continues when one
// document cannot be loaded or embedded.
//
// If a file is missing from the response entirely, no loader claimed it
// (e.g. .pdf today) — it was skipped before any pipeline step ran.
// ==========================================================================

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
