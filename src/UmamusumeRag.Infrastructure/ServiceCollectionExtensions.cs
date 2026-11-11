using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Models;
using UmamusumeRag.Core.Services;
using UmamusumeRag.Infrastructure.Ai;
using UmamusumeRag.Infrastructure.Ingestion;
using UmamusumeRag.Infrastructure.Messaging;
using UmamusumeRag.Infrastructure.Persistence;

namespace UmamusumeRag.Infrastructure;

// ===== The wiring hub =======================================================
// Everything the RAG pipeline needs is registered here. If you are looking for
// "where do I plug X in?", this file is the map:
//
//   IDocumentLoader   step 1  -> TextDocumentLoader, DocxDocumentLoader
//   ITextChunker      step 2  -> RecursiveTextChunker
//   IEmbeddingGenerator 3/5   -> AddOpenAiCompatibleAi
//   IDocumentRepository 4/6   -> PgVectorChunkRepository
//   IChatClient       step 8  -> AddOpenAiCompatibleAi
//   RagOptions        all     -> bound from the "Rag" config section
//
// TWEAK: register your own implementation here to replace any stage (new
//        chunker, new store, OCR/captioning loader, hybrid-search repository).
// ============================================================================

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers EF Core/pgvector storage, the document loaders, the RAG pipeline
    /// services, and the OpenAI-compatible AI clients.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured (see appsettings.json).");

        // Step 4/6 storage. UseVector() teaches Npgsql/EF Core about the
        // pgvector `vector` type and its operators.
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        // All pipeline tuning knobs (chunk size, top-K, data path).
        // TWEAK: values live in src/UmamusumeRag.Api/appsettings.json.
        services.AddSingleton(configuration.GetSection("Rag").Get<RagOptions>() ?? new RagOptions());

        // Step 1: every registered loader is tried for each file, in order.
        // TWEAK: add a loader here (e.g. a PDF or OCR loader) — that is the
        //        whole integration surface for a new file format.
        services.AddSingleton<IDocumentLoader, TextDocumentLoader>();
        services.AddSingleton<IDocumentLoader, DocxDocumentLoader>();

        // Step 2: swap this registration to try a different chunking strategy.
        services.AddSingleton<ITextChunker, RecursiveTextChunker>();

        // Steps 4 and 6: the only PostgreSQL-aware implementation.
        // TWEAK: replace with a different store, or decorate it for caching.
        services.AddScoped<IDocumentRepository, PgVectorChunkRepository>();

        // The pipeline itself (stateless; scoped because the repository is).
        services.AddScoped<DocumentIngestionService>(); // steps 1-4
        services.AddScoped<RagQueryService>();          // steps 5-8

        // Steps 3, 5, 8: chat + embedding clients (LM Studio, OpenAI, ...).
        services.AddOpenAiCompatibleAi(configuration);

        // Entry points: HTTP endpoints send commands/queries through the
        // in-process MassTransit mediator instead of resolving services directly.
        services.AddRagMessaging();

        return services;
    }
}
