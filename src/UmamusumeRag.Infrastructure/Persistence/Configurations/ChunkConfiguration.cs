using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UmamusumeRag.Core.Entities;

namespace UmamusumeRag.Infrastructure.Persistence.Configurations;

// ===== RAG step 4/8 · STORE (schema for the vectors) ========================
// How a chunk becomes a row:
//   Content    -> text column
//   Embedding  -> float[] in the domain, `vector` in PostgreSQL (value converter)
//
// WHY a converter instead of using Pgvector.Vector in the entity: it keeps
// UmamusumeRag.Core free of database packages. The cost is that LINQ distance
// helpers like .CosineDistance() are not available on float[] — which is why
// the search is raw SQL (see PgVectorChunkRepository).
//
// GOTCHA: EF Core creates quoted PascalCase column names ("Embedding", "Id").
//         Any raw SQL must quote them exactly.
// ============================================================================

public class ChunkConfiguration : IEntityTypeConfiguration<Chunk>
{
    /// <summary>
    /// EF Core compares converted collection properties by reference unless told
    /// otherwise; this lets it detect changes to the embedding array (without it,
    /// EF logs a model-validation warning and may miss updates).
    /// </summary>
    private static readonly ValueComparer<float[]> EmbeddingComparer = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right),
        embedding => embedding.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode())),
        embedding => embedding.ToArray());

    public void Configure(EntityTypeBuilder<Chunk> builder)
    {
        builder.ToTable("chunks");
        builder.HasKey(chunk => chunk.Id);

        builder.Property(chunk => chunk.Content).IsRequired();

        // The column is intentionally DIMENSION-LESS ("vector", not "vector(768)"):
        // any embedding model works without a migration.
        //
        // TWEAK: once you settle on a model, lock the dimension and add an ANN
        //        index. Both need a migration; see docs/02-next-steps.md §3.5.
        //        Equivalent EF form:
        //          builder.Property(c => c.Embedding)
        //              .HasColumnType($"vector({dimensions})")
        //              .HasConversion(...);
        //          builder.HasIndex(c => c.Embedding)
        //              .HasMethod("hnsw")
        //              .HasOperators("vector_cosine_ops");
        //
        // GOTCHA: switching embedding models after locking the dimension fails
        //         with "expected N dimensions, not M" until you re-migrate and
        //         re-ingest (?force=true).
        builder.Property(chunk => chunk.Embedding)
            .HasColumnType("vector")
            .HasConversion(new FloatArrayToVectorConverter(), EmbeddingComparer);

        // Prevents duplicate chunk positions when an ingest is retried.
        builder.HasIndex(chunk => new { chunk.DocumentId, chunk.ChunkIndex }).IsUnique();
    }
}
