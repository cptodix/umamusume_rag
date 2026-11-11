using Microsoft.EntityFrameworkCore;
using UmamusumeRag.Core.Entities;

namespace UmamusumeRag.Infrastructure.Persistence;

// ===== RAG step 4/8 · STORE (EF Core context) ==============================
// Two tables: documents (metadata) and chunks (text + vector). The pgvector
// extension is enabled here; the vector column itself is configured in
// ChunkConfiguration.
//
// The schema is created by EF Core migrations (Persistence/Migrations).
// After changing entities/configurations:
//   dotnet ef migrations add <Name> -p src/UmamusumeRag.Infrastructure -s src/UmamusumeRag.Api
// ============================================================================

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Chunk> Chunks => Set<Chunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Enables the `vector` column type used by chunks."Embedding".
        // Requires the pgvector extension in the database (the migration runs
        // CREATE EXTENSION IF NOT EXISTS vector).
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
