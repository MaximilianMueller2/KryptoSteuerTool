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

    private async Task<HttpResponseMessage> Upload(Guid accountId, string fixture, string? source = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "generic-csv", fixture)));
        form.Add(file, "file", fixture);
        form.Add(new StringContent(accountId.ToString()), "accountId");
        if (source != null) form.Add(new StringContent(source), "source");
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
}
