using UmamusumeRag.Core.Entities;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Core.Abstractions;

// ===== RAG steps 4/8 (STORE) and 6/8 (RETRIEVE) =============================
// The boundary between the pipeline and the vector database. Keeping this an
// interface means Core never references EF Core, Npgsql or pgvector, and you
// can swap PostgreSQL for Qdrant/another store without touching the pipeline.
//
// The concrete implementation lives in
// Infrastructure/Persistence/PgVectorChunkRepository.cs — that is where the
// actual SQL (`<=>` cosine search) is.
// ============================================================================

/// <summary>
/// Storage for documents, chunks, and their embeddings.
/// Implemented in the Infrastructure layer with EF Core + PostgreSQL/pgvector.
/// </summary>
public interface IDocumentRepository
{
    /// <summary>
    /// Used by the ingestion pipeline to decide whether a file changed
    /// (content-hash comparison) and to return chunk counts.
    /// </summary>
    Task<Document?> FindBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>Inserts the document, replacing any existing document with the same source path.</summary>
    Task UpsertAsync(Document document, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Vector search: returns the chunks whose embeddings are closest to
    /// <paramref name="queryEmbedding"/> (cosine similarity, best first).
    ///
    /// TWEAK: this signature is intentionally small. If you add hybrid search
    ///        (text + vector) or metadata filters, extend it here first and
    ///        then implement it in PgVectorChunkRepository.
    /// </summary>
    Task<IReadOnlyList<RetrievedChunk>> SearchChunksAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);
}
