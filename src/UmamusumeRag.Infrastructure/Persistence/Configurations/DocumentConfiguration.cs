using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UmamusumeRag.Core.Entities;

namespace UmamusumeRag.Infrastructure.Persistence.Configurations;

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

        builder.HasIndex(document => document.SourcePath).IsUnique();

        builder.HasMany(document => document.Chunks)
            .WithOne(chunk => chunk.Document)
            .HasForeignKey(chunk => chunk.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
