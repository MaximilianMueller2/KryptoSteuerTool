using KryptoSteuer.Api;
using KryptoSteuer.Api.Bitget;
using KryptoSteuer.Api.Endpoints;
using KryptoSteuer.Application;
using KryptoSteuer.Application.Health;
using KryptoSteuer.Importers;
using KryptoSteuer.Infrastructure;
using KryptoSteuer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddImporters();
builder.Services.AddBitgetSync(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

// Migrationen beim Start anwenden (in docker-compose.yml aktiviert); sonst bleibt die DB unberührt.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// Die OpenAPI-Spezifikation (/openapi/v1.json) ist die Quelle für den generierten Angular-Client.
app.MapOpenApi();

app.MapGet("/api/health", async (GetHealthUseCase getHealth, CancellationToken cancellationToken) =>
    {
        var report = await getHealth.ExecuteAsync(ApiInfo.Version, cancellationToken);
        var body = new HealthResponse(
            report.Status,
            report.DatabaseReachable ? "Up" : "Down",
            report.Version,
            report.TimeUtc);

        // 503 bei fehlender Datenbank, damit Docker-Healthchecks und Monitoring anschlagen.
        return report.IsHealthy
            ? Results.Ok(body)
            : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    })
    .WithName("GetHealth")
    .WithSummary("Zustand der API und der Datenbankverbindung")
    .Produces<HealthResponse>(StatusCodes.Status200OK)
    .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

app.MapAccountEndpoints();
app.MapImportEndpoints();
app.MapBitgetEndpoints();
app.MapTaxEndpoints();
app.MapReviewEndpoints();
app.MapReportEndpoints();
app.Run();

/// <summary>Sichtbar für <c>WebApplicationFactory&lt;Program&gt;</c> in den Integrationstests.</summary>
public partial class Program;
