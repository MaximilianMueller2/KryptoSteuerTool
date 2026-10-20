using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace KryptoSteuer.Api.IntegrationTests;

/// <summary>Kompletter Importablauf über HTTP gegen echtes PostgreSQL.</summary>
public sealed class ImportEndpointTests : IAsyncLifetime
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

    private async Task<Guid> CreateAccount()
    {
        var response = await _client.PostAsJsonAsync("/api/accounts",
            new { name = "Testbörse", type = "Exchange", platform = "Generic", country = "de" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("country").GetString().Should().Be("DE");
        return body.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Upload(Guid accountId, string fixture, string? source = null, string folder = "generic-csv", string? timeZone = null) =>
        UploadBytes(accountId, fixture, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", folder, fixture)), source, timeZone);

    private async Task<HttpResponseMessage> UploadBytes(Guid accountId, string name, byte[] bytes, string? source = null, string? timeZone = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", name);
        form.Add(new StringContent(accountId.ToString()), "accountId");
        if (source != null) form.Add(new StringContent(source), "source");
        if (timeZone != null) form.Add(new StringContent(timeZone), "timeZone");
        return await _client.PostAsync("/api/imports", form);
    }

    private async Task<int> CountAsync<T>() where T : class
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<T>().CountAsync();
    }

    [Fact]
    public async Task Upload_Vorschau_Bestaetigung_schreibt_erst_nach_dem_Commit()
    {
        var accountId = await CreateAccount();

        var upload = await Upload(accountId, "valid.csv");
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var preview = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var id = preview.GetProperty("id").GetGuid();
        preview.GetProperty("status").GetString().Should().Be("Pending");
        preview.GetProperty("report").GetProperty("new").GetInt32().Should().Be(6);
        (await CountAsync<Transaction>()).Should().Be(0);
        (await CountAsync<RawRecord>()).Should().Be(0);

        var report = await _client.GetFromJsonAsync<JsonElement>($"/api/imports/{id}");
        report.GetProperty("report").GetProperty("rowsRead").GetInt32().Should().Be(6);

        var commit = await _client.PostAsync($"/api/imports/{id}/commit", null);
        commit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await commit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Committed");
        (await CountAsync<Transaction>()).Should().Be(6);
        (await CountAsync<RawRecord>()).Should().Be(6);
        (await CountAsync<Asset>()).Should().Be(4);

        (await _client.PostAsync($"/api/imports/{id}/commit", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.DeleteAsync($"/api/imports/{id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Zweiter_Upload_derselben_Datei_meldet_nur_Duplikate()
    {
        var accountId = await CreateAccount();
        var first = (await (await Upload(accountId, "valid.csv")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await _client.PostAsync($"/api/imports/{first}/commit", null);

        var second = await (await Upload(accountId, "valid.csv")).Content.ReadFromJsonAsync<JsonElement>();

        var report = second.GetProperty("report");
        report.GetProperty("new").GetInt32().Should().Be(0);
        report.GetProperty("duplicates").GetInt32().Should().Be(6);
    }

    [Fact]
    public async Task Unbestaetigter_Lauf_kann_geloescht_werden()
    {
        var accountId = await CreateAccount();
        var id = (await (await Upload(accountId, "valid.csv")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await _client.DeleteAsync($"/api/imports/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.GetAsync($"/api/imports/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountAsync<ImportRun>()).Should().Be(0);
    }

    [Fact]
    public async Task Fehler_und_unbekannte_Typen_stehen_im_Bericht_und_NeedsReview_in_der_Datenbank()
    {
        var accountId = await CreateAccount();

        var errors = await (await Upload(accountId, "with-errors.csv")).Content.ReadFromJsonAsync<JsonElement>();
        var list = errors.GetProperty("report").GetProperty("errors");
        list.GetArrayLength().Should().Be(5);
        list[0].GetProperty("lineNumber").GetInt32().Should().Be(3);
        list[0].GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();

        var unknown = await (await Upload(accountId, "unknown-type.csv")).Content.ReadFromJsonAsync<JsonElement>();
        await _client.PostAsync($"/api/imports/{unknown.GetProperty("id").GetGuid()}/commit", null);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Transactions.CountAsync(t => t.Status == TransactionStatus.NeedsReview && t.Type == TransactionType.Unknown))
            .Should().Be(3);
    }

    [Fact]
    public async Task Ungueltige_Anfragen_liefern_400_und_404()
    {
        var accountId = await CreateAccount();

        (await Upload(accountId, "not-importable.csv")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Upload(accountId, "valid.csv", "gibt-es-nicht")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Upload(Guid.NewGuid(), "valid.csv")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PostAsJsonAsync("/api/accounts", new { name = "", type = "Wallet", platform = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OpenApi_enthaelt_die_Import_Endpunkte()
    {
        var json = await _client.GetStringAsync("/openapi/v1.json");

        json.Should().Contain("/api/imports").And.Contain("/api/imports/{id}/commit").And.Contain("/api/accounts");
    }

    // ---- Bitget-CSV (Futures) --------------------------------------------------------------------------------

    [Fact]
    public async Task Bitget_Futures_CSV_wird_erkannt_importiert_und_beim_zweiten_Mal_als_Duplikat_gemeldet()
    {
        var accountId = await CreateAccount();

        var preview = await (await Upload(accountId, "futures-usdtm-start.csv", folder: "bitget")).Content.ReadFromJsonAsync<JsonElement>();

        preview.GetProperty("source").GetString().Should().Be("bitget-csv");
        var report = preview.GetProperty("report");
        report.GetProperty("rowsRead").GetInt32().Should().Be(100);
        report.GetProperty("new").GetInt32().Should().Be(100);
        report.GetProperty("errors").GetArrayLength().Should().Be(0);
        report.GetProperty("warnings").EnumerateArray().Single().GetString().Should().Contain("UTC+02:00");
        (await CountAsync<Transaction>()).Should().Be(0);

        await _client.PostAsync($"/api/imports/{preview.GetProperty("id").GetGuid()}/commit", null);
        (await CountAsync<Transaction>()).Should().Be(100);
        (await CountAsync<RawRecord>()).Should().Be(100);

        var again = await (await Upload(accountId, "futures-usdtm-start.csv", folder: "bitget")).Content.ReadFromJsonAsync<JsonElement>();
        again.GetProperty("report").GetProperty("duplicates").GetInt32().Should().Be(100);
        again.GetProperty("report").GetProperty("new").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Formularfeld_timeZone_wird_gespeichert_und_bei_der_Bestaetigung_verwendet()
    {
        var accountId = await CreateAccount();
        var preview = await (await Upload(accountId, "futures-usdtm-mixed.csv", folder: "bitget", timeZone: "UTC")).Content.ReadFromJsonAsync<JsonElement>();
        preview.GetProperty("report").GetProperty("warnings").EnumerateArray().Single().GetString().Should().Contain("laut Option");

        await _client.PostAsync($"/api/imports/{preview.GetProperty("id").GetGuid()}/commit", null);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Transactions.MinAsync(t => t.TimestampUtc)).Should().Be(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        (await db.ImportRuns.SingleAsync()).OptionsJson.Should().Contain("UTC");
    }

    [Fact]
    public async Task Dieselben_Buchungen_aus_Bitget_CSV_und_Bitget_API_werden_nicht_doppelt_gespeichert()
    {
        var accountId = await CreateAccount();
        var csv = await (await Upload(accountId, "futures-usdtm-mixed.csv", folder: "bitget")).Content.ReadFromJsonAsync<JsonElement>();
        await _client.PostAsync($"/api/imports/{csv.GetProperty("id").GetGuid()}/commit", null);
        var before = await CountAsync<Transaction>();

        // dieselbe Funding-Zeile wie in der CSV (2026-03-01 10:00:05 in UTC+2) und eine neue
        var bundle = """
        { "format": "bitget-api-bundle/1", "accountType": "classic", "symbols": [], "sections": [
          { "kind": "futures-bills", "endpoint": "/api/v2/mix/account/bill", "items": [
            { "billId": "A1", "symbol": "BTCUSDT", "amount": "0.5", "fee": "0", "businessType": "contract_settle_fee", "coin": "USDT", "cTime": "1772352005412" },
            { "billId": "A9", "symbol": "BTCUSDT", "amount": "7.77", "fee": "-0.1", "businessType": "close_long", "coin": "USDT", "cTime": "1772445600100" } ] } ] }
        """;
        var api = await (await UploadBytes(accountId, "sync.json", System.Text.Encoding.UTF8.GetBytes(bundle))).Content.ReadFromJsonAsync<JsonElement>();

        api.GetProperty("source").GetString().Should().Be("bitget-api");
        var report = api.GetProperty("report");
        report.GetProperty("duplicates").GetInt32().Should().Be(1);
        report.GetProperty("new").GetInt32().Should().Be(1);
        report.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Contain(w => w!.Contains("anderen Quelle"));

        await _client.PostAsync($"/api/imports/{api.GetProperty("id").GetGuid()}/commit", null);
        (await CountAsync<Transaction>()).Should().Be(before + 1);
    }
}
