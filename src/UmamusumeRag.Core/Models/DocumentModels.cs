namespace UmamusumeRag.Core.Models;

// ===== RAG steps 4/6 · query contracts ====================================
// DocumentSummary -> GET /api/documents (what is in the index)
// RetrievedChunk  -> one vector-search hit, internal to the pipeline
//
// Score is cosine similarity: 1.0 = identical direction, ~0 = unrelated.
// ==========================================================================

/// <summary>Row returned by GET /api/documents.</summary>
public record DocumentSummary(Guid Id, string Title, string SourcePath, int ChunkCount, DateTimeOffset CreatedAt);

/// <summary>A chunk returned by vector search.</summary>
public record RetrievedChunk(
    Guid DocumentId,
    string DocumentTitle,
    string SourcePath,
    int ChunkIndex,
    string Content,
    double Score);
