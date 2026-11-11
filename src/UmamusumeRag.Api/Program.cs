using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using UmamusumeRag.Api.Endpoints;
using UmamusumeRag.Infrastructure;
using UmamusumeRag.Infrastructure.Persistence;

// ===== Application startup ==================================================
// Everything RAG-specific is registered by AddInfrastructure (see
// src/UmamusumeRag.Infrastructure/ServiceCollectionExtensions.cs).
// This file only: configures JSON, registers services, applies migrations in
// development, and maps endpoints.
//
// Useful while learning:
//   - See every SQL statement EF Core runs (including the vector search):
//     set "Microsoft.EntityFrameworkCore.Database.Command": "Information"
//     in appsettings.Development.json.
//   - OpenAPI document: GET /openapi/v1.json (Development only).
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Serialize enums (e.g. IngestStatus) as strings instead of numbers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// RAG pipeline: loaders, chunker, vector store, AI clients, RagOptions.
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Convenience for local development: apply pending EF Core migrations on
    // startup. In production you would run `dotnet ef database update` in a
    // deployment step instead.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.MapGet("/health", async (AppDbContext db) =>
{
    var databaseReachable = await db.Database.CanConnectAsync();
    return databaseReachable
        ? Results.Ok(new { status = "healthy", database = "reachable" })
        : Results.Json(
            new { status = "unhealthy", database = "unreachable" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("Health")
.WithSummary("Checks that the API can reach the PostgreSQL database.");

// RAG write path (steps 1-4) and read path (steps 5-8).
app.MapDocumentsEndpoints();
app.MapAskEndpoints();

app.Run();
