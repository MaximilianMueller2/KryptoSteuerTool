using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace KryptoSteuer.Api.IntegrationTests;

/// <summary>Import → Kurse → Berechnungslauf → Einzelaufstellung über HTTP gegen echtes PostgreSQL.</summary>
public sealed class TaxEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new ApiFactory(_postgres.GetConnectionString(), migrate: true);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task ImportTradeRepublic()
    {
        var created = await _client.PostAsJsonAsync("/api/accounts",
            new { name = "Trade Republic", type = "Broker", platform = "TradeRepublic", country = "DE" });
        var accountId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "traderepublic", "traderepublic-sample.csv");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(File.ReadAllBytes(path)), "file", "traderepublic-sample.csv");
        form.Add(new StringContent(accountId.ToString()), "accountId");
        var upload = await _client.PostAsync("/api/imports", form);
        upload.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var importId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await _client.PostAsync($"/api/imports/{importId}/commit", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Run_is_stored_and_second_run_reports_unchanged_input()
    {
        await ImportTradeRepublic();

        var first = await _client.PostAsJsonAsync("/api/tax/runs", new { year = 2025 });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        firstBody.GetProperty("counts").GetProperty("transactions").GetInt32().Should().BeGreaterThan(0);
        var hash = firstBody.GetProperty("inputHash").GetString();
        hash.Should().HaveLength(64);

        var second = await (await _client.PostAsJsonAsync("/api/tax/runs", new { year = 2025 })).Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("inputHash").GetString().Should().Be(hash);
        second.GetProperty("inputUnchanged").GetBoolean().Should().BeTrue();
        second.GetProperty("diffToPrevious").GetArrayLength().Should().Be(0);

        var id = firstBody.GetProperty("id").GetGuid();
        (await _client.GetAsync($"/api/tax/runs/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"/api/tax/runs/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_year_is_rejected()
    {
        (await _client.PostAsJsonAsync("/api/tax/runs", new { year = 1900 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Disposals_csv_has_bom_and_header()
    {
        await ImportTradeRepublic();
        var response = await _client.GetAsync("/api/tax/years/2025/disposals.csv");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        (await _client.GetAsync("/api/tax/years/2025/disposals")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ecb_upload_and_manual_price_are_stored()
    {
        await ImportTradeRepublic();
        var csv = "Date,USD,JPY,\n2025-01-03,1.04,160,\n2025-01-02,1.03,159,\n";
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "eurofxref-hist.csv");
        var upload = await _client.PostAsync("/api/prices/ecb", form);
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rates").GetInt32().Should().Be(2);

        using var bad = new MultipartFormDataContent();
        bad.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("foo,bar\n1,2\n")), "file", "x.csv");
        (await _client.PostAsync("/api/prices/ecb", bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var manual = await _client.PutAsJsonAsync("/api/prices", new { symbol = "BTC", at = "2025-02-01T00:00:00Z", eurPrice = 90000.5, source = "Handeingabe" });
        manual.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await (await _client.GetAsync("/api/prices")).Content.ReadFromJsonAsync<JsonElement>();
        list.GetArrayLength().Should().BeGreaterThan(0);
    }
}
