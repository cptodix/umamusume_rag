using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UmamusumeRag.Core.Abstractions;
using UmamusumeRag.Core.Models;
using UmamusumeRag.Core.Services;
using UmamusumeRag.Infrastructure.Ai;
using UmamusumeRag.Infrastructure.Ingestion;
using UmamusumeRag.Infrastructure.Persistence;

namespace UmamusumeRag.Infrastructure;

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

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        // Bound from the "Rag" section of appsettings.json.
        services.AddSingleton(configuration.GetSection("Rag").Get<RagOptions>() ?? new RagOptions());

        // Ingestion: every registered IDocumentLoader is tried for each file.
        services.AddSingleton<IDocumentLoader, TextDocumentLoader>();
        services.AddSingleton<IDocumentLoader, DocxDocumentLoader>();
        services.AddSingleton<ITextChunker, RecursiveTextChunker>();

        // Storage + pipeline.
        services.AddScoped<IDocumentRepository, PgVectorChunkRepository>();
        services.AddScoped<DocumentIngestionService>();
        services.AddScoped<RagQueryService>();

        // Chat + embedding clients (LM Studio, OpenAI, ... via configuration).
        services.AddOpenAiCompatibleAi(configuration);

        return services;
    }
}
