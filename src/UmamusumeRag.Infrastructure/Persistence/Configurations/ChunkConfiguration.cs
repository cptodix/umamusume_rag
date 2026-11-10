using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UmamusumeRag.Core.Entities;

namespace UmamusumeRag.Infrastructure.Persistence.Configurations;

public class ChunkConfiguration : IEntityTypeConfiguration<Chunk>
{
    /// <summary>
    /// EF Core compares converted collection properties by reference unless told otherwise;
    /// this lets it detect changes to the embedding array.
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

        // The domain model keeps the embedding as a float[]; in PostgreSQL it is a
        // pgvector `vector` column. The column is intentionally dimension-less so any
        // embedding model works — see the README for locking dimensions + adding an
        // HNSW index once you settle on a model.
        builder.Property(chunk => chunk.Embedding)
            .HasColumnType("vector")
            .HasConversion(new FloatArrayToVectorConverter(), EmbeddingComparer);

        builder.HasIndex(chunk => new { chunk.DocumentId, chunk.ChunkIndex }).IsUnique();
    }
}
