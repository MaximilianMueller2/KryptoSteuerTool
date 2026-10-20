using System.Net;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;

namespace KryptoSteuer.Api.IntegrationTests;

/// <summary>Braucht einen laufenden Docker-Daemon (Testcontainers).</summary>
public sealed class HealthEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Health_liefert_200_und_Database_Up_wenn_Postgres_erreichbar_ist()
    {
        await using var factory = new ApiFactory(_postgres.GetConnectionString());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Healthy");
        body.Database.Should().Be("Up");
        body.TimeUtc.Offset.Should().Be(TimeSpan.Zero, "Zeitstempel werden in UTC ausgegeben");
    }

    [Fact]
    public async Task OpenApi_Dokument_enthaelt_den_Health_Endpunkt()
    {
        await using var factory = new ApiFactory(_postgres.GetConnectionString());
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/openapi/v1.json");

        json.Should().Contain("/api/health");
    }
}
