namespace UmamusumeRag.Core.Entities;

// ===== RAG step 4/8 · STORE (aggregate root) ===============================
// One Document = one source file. Chunks hang off it and carry the vectors.
//
// WHY ContentHash: lets ingestion skip the expensive embedding call when the
// extracted text has not changed since the last run (see
// DocumentIngestionService). It is a cache key, not business data.
// ==========================================================================

/// <summary>
/// A source document that has been ingested into the RAG pipeline.
/// </summary>
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-readable title; shown to the model and in API sources.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Full path of the source file; identifies the document across re-ingests.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the extracted text, used to skip re-embedding unchanged files.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Chunk> Chunks { get; set; } = [];
}
