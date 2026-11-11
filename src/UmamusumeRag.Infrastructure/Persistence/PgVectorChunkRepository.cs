using Microsoft.EntityFrameworkCore;
using Pgvector;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Entities;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Infrastructure.Persistence;

// ===== RAG steps 4/8 (STORE) and 6/8 (RETRIEVE) =============================
// The only place in the system that talks to PostgreSQL/pgvector.
//
// Step 4 (UpsertAsync) is plain EF Core: documents + chunks + float[] vectors
// via a value converter (see ChunkConfiguration).
//
// Step 6 (SearchChunksAsync) is deliberately raw SQL so the vector math is
// visible. That SQL is the heart of the "retrieval" half of RAG.
//
// GOTCHA: EF Core created the columns with quoted PascalCase names ("Id",
//         "Embedding", ...). Raw SQL MUST quote them — unquoted identifiers
//         fold to lowercase and fail with "column d.id does not exist".
// ============================================================================

public class PgVectorChunkRepository(AppDbContext db) : IDocumentRepository
{
    public Task<Document?> FindBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        db.Documents
            .AsNoTracking() // read-only: used for the content-hash check and chunk count
            .Include(document => document.Chunks) // needed for the "unchanged" chunk count + potential in-place updates
            .FirstOrDefaultAsync(document => document.SourcePath == sourcePath, cancellationToken);

    // ---- RAG step 4/8 · STORE ----------------------------------------------
    // Replace-by-source-path. Deleting the old document cascades to its chunks,
    // so a re-ingest can never leave stale vectors behind.
    //
    // WHY tracked remove + insert in one SaveChanges instead of ExecuteDelete:
    // both statements then run in one transaction, so a failed insert cannot
    // leave the file unindexed.
    //
    // TWEAK: if you add chunk-level metadata (page, section, kind), it is
    //        persisted automatically through the Chunk entity.
    public async Task UpsertAsync(Document document, CancellationToken cancellationToken = default)
    {
        var existing = await db.Documents
            .FirstOrDefaultAsync(candidate => candidate.SourcePath == document.SourcePath, cancellationToken);

        if (existing is not null)
            db.Documents.Remove(existing); // its chunks are removed by the cascade delete

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
    }

    // Delete is a set-based LINQ operation: EF Core translates it to a single
    // DELETE statement, and the database cascade removes the chunks.
    // (The tracked alternative — FindAsync + Remove + SaveChanges — would first
    // SELECT the row; this is one round trip.)
    public async Task<bool> RemoveAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var deleted = await db.Documents
            .Where(document => document.Id == documentId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Documents
            .AsNoTracking() // projection only, nothing is tracked
            .OrderBy(document => document.Title)
            .Select(document => new DocumentSummary(
                document.Id,
                document.Title,
                document.SourcePath,
                document.Chunks.Count,
                document.CreatedAt))
            .ToListAsync(cancellationToken);

    // ---- RAG step 6/8 · RETRIEVE (the actual vector search) -----------------
    //
    // `<=>` is pgvector's COSINE DISTANCE operator:
    //     0 = identical direction, 2 = opposite direction
    // Similarity = 1 - distance, so the API reports 1.0 = best, ~0.0 = unrelated.
    //
    // TWEAK (each one is a real experiment, see docs/02-next-steps.md):
    //   - Metric: replace `<=>` with `<->` (L2) or `<#>` (negative inner product).
    //             Then also swap vector_cosine_ops for vector_l2_ops/vector_ip_ops
    //             when you add an index.
    //   - Index: add an HNSW/IVFFlat index (requires locking the column
    //            dimension first) — see docs/02-next-steps.md §3.5.
    //   - Hybrid: run a second query with to_tsvector/websearch_to_tsquery and
    //            merge the ranked lists with Reciprocal Rank Fusion (§3.6).
    //   - Rerank: increase topK here, then re-score in RagQueryService (§3.7).
    //
    // No ANN index exists yet, so this is a sequential scan. Fine for a practice
    // corpus; EXPLAIN ANALYZE it once you have real data.
    public async Task<IReadOnlyList<RetrievedChunk>> SearchChunksAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var queryVector = new Vector(queryEmbedding.ToArray());

        // SqlQuery<T> maps the result set to an unmapped row type and
        // parameterises {queryVector} / {topK} (no SQL injection).
        var rows = await db.Database
            .SqlQuery<ChunkSearchRow>($"""
                SELECT c."DocumentId"  AS "DocumentId",
                       d."Title"       AS "DocumentTitle",
                       d."SourcePath"  AS "SourcePath",
                       c."ChunkIndex"  AS "ChunkIndex",
                       c."Content"     AS "Content",
                       1 - (c."Embedding" <=> {queryVector}) AS "Score"
                FROM chunks c
                JOIN documents d ON d."Id" = c."DocumentId"
                ORDER BY c."Embedding" <=> {queryVector}
                LIMIT {topK}
                """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new RetrievedChunk(
                row.DocumentId,
                row.DocumentTitle,
                row.SourcePath,
                row.ChunkIndex,
                row.Content,
                row.Score))
            .ToList();
    }
}

/// <summary>
/// Row shape returned by <see cref="PgVectorChunkRepository.SearchChunksAsync"/>.
/// Property names must match the SQL aliases exactly.
/// </summary>
internal sealed class ChunkSearchRow
{
    public Guid DocumentId { get; set; }

    public string DocumentTitle { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    public double Score { get; set; }
}
