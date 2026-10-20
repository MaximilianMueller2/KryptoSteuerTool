using System.Net;
using System.Net.Http.Json;

namespace KryptoSteuer.Api.IntegrationTests;

public sealed class HealthWithoutDatabaseTests
{
    // Port 1 ist nicht belegt; Timeout=1 hält den Test kurz.
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Command Timeout=1";

    [Fact]
    public async Task Health_liefert_503_und_Database_Down_wenn_Postgres_fehlt()
    {
        await using var factory = new ApiFactory(UnreachableConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Unhealthy");
        body.Database.Should().Be("Down");
    }
}
