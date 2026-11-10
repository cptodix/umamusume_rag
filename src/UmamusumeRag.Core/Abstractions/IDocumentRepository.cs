using UmamusumeRag.Core.Entities;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Core.Abstractions;

/// <summary>
/// Storage for documents, chunks, and their embeddings.
/// Implemented in the Infrastructure layer with EF Core + PostgreSQL/pgvector.
/// </summary>
public interface IDocumentRepository
{
    Task<Document?> FindBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>Inserts the document, replacing any existing document with the same source path.</summary>
    Task UpsertAsync(Document document, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Vector search: returns the chunks whose embeddings are closest to
    /// <paramref name="queryEmbedding"/> (cosine similarity, best first).
    /// </summary>
    Task<IReadOnlyList<RetrievedChunk>> SearchChunksAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);
}
