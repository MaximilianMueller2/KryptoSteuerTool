using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KryptoSteuer.Api.Bitget;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Importers.Bitget;
using KryptoSteuer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace KryptoSteuer.Api.IntegrationTests;

/// <summary>Bitget-Anbindung über HTTP und PostgreSQL; die Börse wird durch einen skriptbaren Client ersetzt (keine echten Keys).</summary>
public sealed class BitgetEndpointTests : IAsyncLifetime
{
    private sealed class ScriptedBitgetClient : IBitgetClient
    {
        public Dictionary<string, string> Routes { get; } = new()
        {
            ["/api/v3/account/info"] = "!40084",
            ["/api/v2/spot/account/info"] = "{\"authorities\":[\"readonly\"]}",
            ["/api/v2/spot/public/symbols"] = "[{\"symbol\":\"BTCUSDT\",\"baseCoin\":\"BTC\",\"quoteCoin\":\"USDT\"}]",
            ["/api/v2/spot/trade/history-orders"] = "[{\"orderId\":\"o1\",\"symbol\":\"BTCUSDT\"}]",
            ["/api/v2/spot/trade/fills"] =
                "[{\"tradeId\":\"t1\",\"symbol\":\"BTCUSDT\",\"side\":\"buy\",\"size\":\"0.01\",\"amount\":\"600\",\"cTime\":\"1767225600000\"," +
                "\"feeDetail\":{\"feeCoin\":\"BTC\",\"totalFee\":\"-0.00001\"}}]",
            ["/api/v2/spot/account/bills"] = "[]",
            ["/api/v2/spot/wallet/deposit-records"] =
                "[{\"orderId\":\"d1\",\"coin\":\"USDT\",\"size\":\"1000\",\"status\":\"success\",\"cTime\":\"1767139200000\"}]",
            ["/api/v2/spot/wallet/withdrawal-records"] = "[]",
            ["/api/v2/convert/convert-record"] = "{\"dataList\":[],\"endId\":\"\"}",
            ["/api/v2/mix/account/bill"] = "{\"bills\":[],\"endId\":\"\"}",
            ["/api/v2/earn/savings/records"] = "{\"resultList\":[],\"endId\":\"\"}",
            ["/api/v2/spot/account/assets"] =
                "[{\"coin\":\"BTC\",\"available\":\"0.5\",\"frozen\":\"0\",\"locked\":\"0\"},{\"coin\":\"USDT\",\"available\":\"400\",\"frozen\":\"0\",\"locked\":\"0\"}]",
            ["/api/v2/mix/account/accounts"] = "[]",
            ["/api/v2/earn/account/assets"] = "[]",
            ["/api/v2/account/funding-assets"] = "[]",
        };

        public Task<JsonElement> GetAsync(string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken) =>
            Respond(path);

        public Task<JsonElement> GetPublicAsync(string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken) =>
            Respond(path);

        private Task<JsonElement> Respond(string path)
        {
            var body = Routes.GetValueOrDefault(path) ?? throw new BitgetApiException("40404", $"unbekannter Pfad {path}");
            if (body.StartsWith('!')) throw new BitgetApiException(body[1..], "nicht verfügbar");
            return Task.FromResult(JsonDocument.Parse(body).RootElement.Clone());
        }
    }

    private sealed class ScriptedFactory(ScriptedBitgetClient client) : IBitgetClientFactory
    {
        public List<BitgetCredentials> Created { get; } = [];

        public IBitgetClient Create(BitgetCredentials credentials)
        {
            Created.Add(credentials);
            return client;
        }
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly ScriptedBitgetClient _bitget = new();
    private ScriptedFactory _factory = null!;
    private WebApplicationFactory<Program> _app = null!;
    private HttpClient _client = null!;
    private string _keysPath = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _keysPath = Path.Combine(Path.GetTempPath(), "ks-keys-" + Guid.NewGuid());
        _factory = new ScriptedFactory(_bitget);
        _app = new ApiFactory(_postgres.GetConnectionString(), migrate: true).WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeysPath"] = _keysPath }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IBitgetClientFactory>(_factory);
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));
            });
        });
        _client = _app.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(_keysPath)) Directory.Delete(_keysPath, recursive: true);
    }

    private async Task<Guid> CreateAccount()
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { name = "Bitget", type = "Exchange", platform = "Bitget", country = "SC" });
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> PutKey(Guid accountId, string key = "KEY-123", string secret = "SECRET-456", string pass = "PASS-789") =>
        _client.PutAsJsonAsync($"/api/accounts/{accountId}/bitget", new { apiKey = key, secret, passphrase = pass });

    [Fact]
    public async Task Key_wird_geprueft_verschluesselt_gespeichert_und_nie_zurueckgegeben()
    {
        var accountId = await CreateAccount();

        var response = await PutKey(accountId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("KEY-123").And.NotContain("SECRET-456").And.NotContain("PASS-789");
        JsonDocument.Parse(body).RootElement.GetProperty("accountType").GetString().Should().Be("classic");

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ExchangeCredentials.SingleAsync();
        new[] { stored.EncryptedApiKey, stored.EncryptedSecret, stored.EncryptedPassphrase }
            .Should().OnlyContain(v => !v.Contains("KEY-123") && !v.Contains("SECRET-456") && !v.Contains("PASS-789") && v.Length > 20);

        var status = await _client.GetStringAsync($"/api/accounts/{accountId}/bitget");
        status.Should().NotContain("KEY-123").And.NotContain("SECRET-456").And.Contain("\"configured\":true");
        Directory.GetFiles(_keysPath).Should().NotBeEmpty("der Schlüsselring liegt außerhalb der Datenbank im Dateisystem");
    }

    [Fact]
    public async Task Warnung_bei_zu_weitreichenden_Rechten_steht_in_der_Antwort_und_im_Status()
    {
        var accountId = await CreateAccount();
        _bitget.Routes["/api/v2/spot/account/info"] = "{\"authorities\":[\"readonly\",\"withdraw\"]}";

        var body = await (await PutKey(accountId)).Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("warnings").EnumerateArray().Single().GetString().Should().Contain("Leserechten");
        var status = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/bitget");
        status.GetProperty("warnings").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Abgelehnter_Key_liefert_400_und_wird_nicht_gespeichert()
    {
        var accountId = await CreateAccount();
        _bitget.Routes["/api/v3/account/info"] = "!40012";
        _bitget.Routes["/api/v2/spot/account/info"] = "!40012";

        var response = await PutKey(accountId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("SECRET-456");
        (await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/bitget")).GetProperty("configured").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Jetzt_synchronisieren_importiert_Buchungen_und_meldet_Saldo_Abweichungen_in_der_Review_Queue()
    {
        var accountId = await CreateAccount();
        await PutKey(accountId);

        var response = await _client.PostAsync($"/api/accounts/{accountId}/bitget/sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("import").GetProperty("new").GetInt32().Should().Be(2);
        result.GetProperty("balanceMismatches").EnumerateArray().Single().GetProperty("symbol").GetString().Should().Be("BTC");

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Transactions.CountAsync()).Should().Be(2);
            (await db.RawRecords.CountAsync()).Should().Be(2);
            (await db.SyncStates.CountAsync()).Should().Be(BitgetFetcher.ExpectedSections(BitgetAccountType.Classic).Count);
            (await db.ImportRuns.SingleAsync()).Source.Should().Be("bitget-api");
        }

        var review = await _client.GetFromJsonAsync<JsonElement>("/api/review-items");
        var item = review.EnumerateArray().Single();
        item.GetProperty("kind").GetString().Should().Be("BalanceMismatch");
        item.GetProperty("reason").GetString().Should().Contain("BTC").And.Contain("0.5");

        var status = await _client.GetFromJsonAsync<JsonElement>($"/api/accounts/{accountId}/bitget");
        status.GetProperty("lastSyncSummary").GetString().Should().Contain("2 neue Buchungen");
    }

    [Fact]
    public async Task Zweiter_Sync_erzeugt_keine_Duplikate_und_schliesst_behobene_Abweichungen()
    {
        var accountId = await CreateAccount();
        await PutKey(accountId);
        await _client.PostAsync($"/api/accounts/{accountId}/bitget/sync", null);

        // Abweichung behoben: Bitget-Saldo entspricht jetzt dem berechneten Bestand.
        _bitget.Routes["/api/v2/spot/account/assets"] =
            "[{\"coin\":\"BTC\",\"available\":\"0.00999\",\"frozen\":\"0\",\"locked\":\"0\"},{\"coin\":\"USDT\",\"available\":\"400\",\"frozen\":\"0\",\"locked\":\"0\"}]";
        var second = await (await _client.PostAsync($"/api/accounts/{accountId}/bitget/sync", null)).Content.ReadFromJsonAsync<JsonElement>();

        second.GetProperty("import").GetProperty("new").GetInt32().Should().Be(0);
        second.GetProperty("import").GetProperty("duplicates").GetInt32().Should().Be(2);
        second.GetProperty("balanceMismatches").GetArrayLength().Should().Be(0);
        (await _client.GetFromJsonAsync<JsonElement>("/api/review-items")).GetArrayLength().Should().Be(0);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Transactions.CountAsync()).Should().Be(2);
        (await db.ReviewItems.CountAsync(r => r.Status == ReviewItemStatus.Resolved)).Should().Be(1);
    }

    [Fact]
    public async Task Mehrere_Syncs_mit_gleicher_Abweichung_aktualisieren_den_Eintrag_statt_Dubletten_anzulegen()
    {
        var accountId = await CreateAccount();
        await PutKey(accountId);
        await _client.PostAsync($"/api/accounts/{accountId}/bitget/sync", null);
        await _client.PostAsync($"/api/accounts/{accountId}/bitget/sync", null);

        (await _client.GetFromJsonAsync<JsonElement>("/api/review-items")).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Sync_ohne_Key_liefert_400_und_Loeschen_entfernt_Key_und_Sync_Zustand_aber_nicht_die_Buchungen()
    {
        var accountId = await CreateAccount();
        (await _client.PostAsync($"/api/accounts/{accountId}/bitget/sync", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await PutKey(accountId);
        await _client.PostAsync($"/api/accounts/{accountId}/bitget/sync", null);
        (await _client.DeleteAsync($"/api/accounts/{accountId}/bitget")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ExchangeCredentials.CountAsync()).Should().Be(0);
        (await db.SyncStates.CountAsync()).Should().Be(0);
        (await db.Transactions.CountAsync()).Should().Be(2);
        (await _client.DeleteAsync($"/api/accounts/{accountId}/bitget")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OpenApi_enthaelt_die_Bitget_Endpunkte()
    {
        var json = await _client.GetStringAsync("/openapi/v1.json");

        json.Should().Contain("/api/accounts/{accountId}/bitget").And.Contain("/api/accounts/{accountId}/bitget/sync").And.Contain("/api/review-items");
    }
}

public class BitgetDailySyncScheduleTests
{
    private static readonly TimeSpan RunAt = new(3, 30, 0);

    [Fact]
    public void Naechster_Lauf_ist_heute_wenn_die_Uhrzeit_noch_bevorsteht()
    {
        BitgetDailySyncService.NextRun(new DateTimeOffset(2026, 10, 20, 1, 0, 0, TimeSpan.Zero), RunAt)
            .Should().Be(new DateTimeOffset(2026, 10, 20, 3, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Naechster_Lauf_ist_morgen_wenn_die_Uhrzeit_vorbei_ist()
    {
        BitgetDailySyncService.NextRun(new DateTimeOffset(2026, 10, 20, 3, 30, 0, TimeSpan.Zero), RunAt)
            .Should().Be(new DateTimeOffset(2026, 10, 21, 3, 30, 0, TimeSpan.Zero));
        BitgetDailySyncService.NextRun(new DateTimeOffset(2026, 10, 20, 23, 0, 0, TimeSpan.Zero), RunAt)
            .Should().Be(new DateTimeOffset(2026, 10, 21, 3, 30, 0, TimeSpan.Zero));
    }
}
