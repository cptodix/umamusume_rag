using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UmamusumeRag.Core.Entities;

namespace UmamusumeRag.Infrastructure.Persistence.Configurations;

// ===== RAG step 4/8 · STORE (documents schema) =============================
// Unique SourcePath is what makes ingestion idempotent: one row per file, so
// UpsertAsync can replace it. Cascade delete guarantees a re-ingest or delete
// never leaves orphaned chunks (and therefore stale vectors) behind.
// ==========================================================================

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(document => document.Id);

        builder.Property(document => document.Title).IsRequired().HasMaxLength(512);
        builder.Property(document => document.SourcePath).IsRequired().HasMaxLength(1024);
        builder.Property(document => document.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(document => document.CreatedAt).IsRequired();

        // One document per source file; enables replace-on-reingest.
        builder.HasIndex(document => document.SourcePath).IsUnique();

        // Deleting a document deletes its chunks (and their vectors) atomically.
        builder.HasMany(document => document.Chunks)
            .WithOne(chunk => chunk.Document)
            .HasForeignKey(chunk => chunk.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
