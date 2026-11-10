using Microsoft.EntityFrameworkCore;
using Pgvector;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Entities;
using UmamusumeRag.Core.Models;

namespace UmamusumeRag.Infrastructure.Persistence;

public class PgVectorChunkRepository(AppDbContext db) : IDocumentRepository
{
    public Task<Document?> FindBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        db.Documents
            .Include(document => document.Chunks)
            .FirstOrDefaultAsync(document => document.SourcePath == sourcePath, cancellationToken);

    public async Task UpsertAsync(Document document, CancellationToken cancellationToken = default)
    {
        var existing = await db.Documents
            .FirstOrDefaultAsync(candidate => candidate.SourcePath == document.SourcePath, cancellationToken);

        if (existing is not null)
            db.Documents.Remove(existing); // its chunks are removed by the cascade delete

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.FindAsync([documentId], cancellationToken);
        if (document is null)
            return false;

        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Documents
            .OrderBy(document => document.Title)
            .Select(document => new DocumentSummary(
                document.Id,
                document.Title,
                document.SourcePath,
                document.Chunks.Count,
                document.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RetrievedChunk>> SearchChunksAsync(
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var queryVector = new Vector(queryEmbedding.ToArray());

        // `<=>` is pgvector's cosine-distance operator. The raw SQL keeps the vector
        // math explicit; Entity Framework Core would translate the equivalent
        // `.CosineDistance(...)` LINQ call the same way. Similarity (1 = identical,
        // 0 = unrelated) is what we report as the score.
        // No ANN index yet: for a practice corpus a sequential scan is fine.
        // See the README for how to lock dimensions and add an HNSW index later.
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

/// <summary>Row shape returned by <see cref="PgVectorChunkRepository.SearchChunksAsync"/>.</summary>
internal sealed class ChunkSearchRow
{
    public Guid DocumentId { get; set; }

    public string DocumentTitle { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    public double Score { get; set; }
}
