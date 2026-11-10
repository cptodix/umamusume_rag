using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using UmamusumeRag.Api.Endpoints;
using UmamusumeRag.Infrastructure;
using UmamusumeRag.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Serialize enums (e.g. IngestStatus) as strings instead of numbers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Convenience for local development: apply pending EF Core migrations on startup.
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

app.MapDocumentsEndpoints();
app.MapAskEndpoints();

app.Run();
