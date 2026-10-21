using System.Globalization;
using KryptoSteuer.Application.Bitget;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Importers.Bitget;
using Microsoft.Extensions.Time.Testing;

namespace KryptoSteuer.Importers.Tests.Bitget;

public class BitgetSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryImportStore _importStore = new();
    private readonly InMemoryBitgetSyncStore _syncStore;
    private readonly FakeBitgetClient _client;
    private readonly FakeBitgetClientFactory _factory;
    private readonly BitgetSyncService _service;

    public BitgetSyncServiceTests()
    {
        _syncStore = new InMemoryBitgetSyncStore(_importStore);
        _client = new FakeBitgetClient()
            .Route("/api/v3/account/info", _ => throw new BitgetApiException("40084", "not uta"))
            .Route("/api/v2/spot/account/info", "{\"authorities\":[\"readonly\"]}")
            .Route("/api/v2/spot/public/symbols", "[{\"symbol\":\"BTCUSDT\",\"baseCoin\":\"BTC\",\"quoteCoin\":\"USDT\"}]")
            .Route("/api/v2/spot/trade/history-orders", "[{\"orderId\":\"o1\",\"symbol\":\"BTCUSDT\"}]")
            .Route("/api/v2/spot/trade/fills",
                "[{\"tradeId\":\"t1\",\"symbol\":\"BTCUSDT\",\"side\":\"buy\",\"size\":\"0.01\",\"amount\":\"600\",\"cTime\":\"1767225600000\"," +
                "\"feeDetail\":{\"feeCoin\":\"BTC\",\"totalFee\":\"-0.00001\"}}]")
            .Route("/api/v2/spot/account/bills", "[]")
            .Route("/api/v2/spot/wallet/deposit-records",
                "[{\"orderId\":\"d1\",\"coin\":\"USDT\",\"size\":\"1000\",\"status\":\"success\",\"cTime\":\"1767139200000\"}]")
            .Route("/api/v2/spot/wallet/withdrawal-records", "[]")
            .Route("/api/v2/convert/convert-record", "{\"dataList\":[],\"endId\":\"\"}")
            .Route("/api/v2/mix/account/bill", "{\"bills\":[],\"endId\":\"\"}")
            .Route("/api/v2/earn/savings/records", "{\"resultList\":[],\"endId\":\"\"}")
            // Salden: Spot, Futures, Earn, Funding
            .Route("/api/v2/spot/account/assets",
                "[{\"coin\":\"BTC\",\"available\":\"0.00999\",\"frozen\":\"0\",\"locked\":\"0\"},{\"coin\":\"USDT\",\"available\":\"400\",\"frozen\":\"0\",\"locked\":\"0\"}]")
            .Route("/api/v2/mix/account/accounts", "[]")
            .Route("/api/v2/earn/account/assets", "[]")
            .Route("/api/v2/account/funding-assets", "[]");
        _factory = new FakeBitgetClientFactory(_client);
        _service = new BitgetSyncService(_syncStore,
            _importStore, new ImportService([new BitgetApiImporter()], _importStore, _clock),
            new FakeSecretProtector(), _factory, _clock);
    }

    private static BitgetCredentials Key => new("my-key", "my-secret", "my-pass");

    // ---- Zugangsdaten ------------------------------------------------------------------------------------

    [Fact]
    public async Task Speichert_Zugangsdaten_nur_verschluesselt_und_meldet_den_Kontotyp()
    {
        var check = await _service.SaveCredentialsAsync(_importStore.AccountId, Key, CancellationToken.None);

        check.AccountType.Should().Be("classic");
        check.Warnings.Should().BeEmpty();
        var stored = _syncStore.Credentials[_importStore.AccountId];
        new[] { stored.EncryptedApiKey, stored.EncryptedSecret, stored.EncryptedPassphrase }
            .Should().OnlyContain(v => !v.Contains("my-key") && !v.Contains("my-secret") && !v.Contains("my-pass") && v.StartsWith("enc("));
    }

    [Fact]
    public async Task Warnt_bei_Handelsrechten_speichert_den_Key_aber()
    {
        _client.Route("/api/v2/spot/account/info", "{\"authorities\":[\"readonly\",\"spot_trade\"]}");

        var check = await _service.SaveCredentialsAsync(_importStore.AccountId, Key, CancellationToken.None);

        check.Warnings.Should().ContainSingle().Which.Should().Contain("Leserechten");
        _syncStore.Credentials[_importStore.AccountId].PermissionWarnings.Should().Contain("Leserechten");
    }

    [Fact]
    public async Task Ein_von_Bitget_abgelehnter_Key_wird_nicht_gespeichert_und_nicht_im_Fehler_ausgegeben()
    {
        _client.Route("/api/v3/account/info", _ => throw new BitgetApiException("40012", "apikey/password is incorrect"));
        _client.Route("/api/v2/spot/account/info", _ => throw new BitgetApiException("40012", "apikey/password is incorrect"));

        var act = () => _service.SaveCredentialsAsync(_importStore.AccountId, Key, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<BitgetSyncException>()).Which;
        ex.Message.Should().Contain("apikey/password is incorrect").And.NotContain("my-secret").And.NotContain("my-key");
        _syncStore.Credentials.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "s", "p")]
    [InlineData("k", "", "p")]
    [InlineData("k", "s", " ")]
    public async Task Unvollstaendige_Zugangsdaten_werden_abgelehnt(string key, string secret, string pass)
    {
        var act = () => _service.SaveCredentialsAsync(_importStore.AccountId, new BitgetCredentials(key, secret, pass), CancellationToken.None);

        await act.Should().ThrowAsync<BitgetSyncException>();
    }

    [Fact]
    public async Task Unbekanntes_Konto_wird_abgelehnt()
    {
        var act = () => _service.SaveCredentialsAsync(Guid.NewGuid(), Key, CancellationToken.None);

        await act.Should().ThrowAsync<BitgetSyncException>();
    }

    // ---- Sync --------------------------------------------------------------------------------------------

    private async Task<BitgetSyncResult> FirstSync()
    {
        await _service.SaveCredentialsAsync(_importStore.AccountId, Key, CancellationToken.None);
        return await _service.SyncAsync(_importStore.AccountId, CancellationToken.None);
    }

    [Fact]
    public async Task Sync_ohne_Key_schlaegt_mit_klarer_Meldung_fehl()
    {
        var act = () => _service.SyncAsync(_importStore.AccountId, CancellationToken.None);

        (await act.Should().ThrowAsync<BitgetSyncException>()).Which.Message.Should().Contain("kein Bitget-Key");
    }

    [Fact]
    public async Task Erster_Sync_holt_90_Tage_schreibt_Buchungen_und_entschluesselt_den_Key_nur_fuer_den_Client()
    {
        var result = await FirstSync();

        result.Import.New.Should().Be(2);
        result.Import.Errors.Should().BeEmpty();
        _importStore.Transactions.Select(t => t.Type).Should().BeEquivalentTo([TransactionType.Buy, TransactionType.Deposit]);
        _importStore.RawRecords.Should().HaveCount(2);
        _importStore.Runs.Values.Should().ContainSingle().Which.Source.Should().Be("bitget-api");
        _factory.Created.Should().OnlyContain(c => c.ApiKey == "my-key" && c.Secret == "my-secret" && c.Passphrase == "my-pass");

        var starts = _client.CallsTo("/api/v2/spot/account/bills")
            .Select(c => long.Parse(c.Query["startTime"], CultureInfo.InvariantCulture)).Min();
        starts.Should().Be(Now.AddDays(-90).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Sync_speichert_je_Endpunkt_den_letzten_Erfolg_und_den_Status_am_Key()
    {
        await FirstSync();

        var states = _syncStore.States[_importStore.AccountId];
        states.Keys.Should().BeEquivalentTo(BitgetFetcher.ExpectedSections(BitgetAccountType.Classic));
        states.Values.Should().OnlyContain(v => v == Now);
        var credential = _syncStore.Credentials[_importStore.AccountId];
        credential.LastSyncAtUtc.Should().Be(Now);
        credential.LastSyncSummary.Should().Contain("2 neue Buchungen");
    }

    [Fact]
    public async Task Zweiter_Sync_beginnt_mit_7_Tagen_Ueberlappung_und_erzeugt_keine_Duplikate()
    {
        await FirstSync();
        _client.Calls.Clear();
        _clock.Advance(TimeSpan.FromDays(1));

        var second = await _service.SyncAsync(_importStore.AccountId, CancellationToken.None);

        second.Import.New.Should().Be(0);
        second.Import.Duplicates.Should().Be(2);
        _importStore.Transactions.Should().HaveCount(2);
        var start = _client.CallsTo("/api/v2/spot/account/bills")
            .Select(c => long.Parse(c.Query["startTime"], CultureInfo.InvariantCulture)).Min();
        start.Should().Be((Now - BitgetSyncService.Overlap).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Ausgefallener_Endpunkt_wird_gemeldet_und_sein_Zeitpunkt_nicht_vorgezogen()
    {
        _client.Route("/api/v2/earn/savings/records", _ => throw new BitgetApiException("40054", "earn not enabled"));

        var result = await FirstSync();

        result.FailedSections.Should().ContainSingle().Which.Should().Contain("earn-savings");
        _syncStore.States[_importStore.AccountId].Should().NotContainKey("earn-savings").And.ContainKey("spot-fills");

        // Nächster Sync: für den fehlenden Endpunkt gibt es keinen Zeitpunkt, also wieder 90 Tage.
        _client.Calls.Clear();
        _clock.Advance(TimeSpan.FromDays(1));
        await _service.SyncAsync(_importStore.AccountId, CancellationToken.None);
        var start = _client.CallsTo("/api/v2/spot/account/bills")
            .Select(c => long.Parse(c.Query["startTime"], CultureInfo.InvariantCulture)).Min();
        start.Should().Be(_clock.GetUtcNow().AddDays(-90).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Bitget_Fehler_beim_Abruf_wird_gemeldet_und_am_Key_vermerkt()
    {
        await _service.SaveCredentialsAsync(_importStore.AccountId, Key, CancellationToken.None);
        _client.Route("/api/v3/account/info", _ => throw new BitgetApiException("429", "rate limited"));
        _client.Route("/api/v2/spot/account/info", _ => throw new BitgetApiException("429", "rate limited"));

        var act = () => _service.SyncAsync(_importStore.AccountId, CancellationToken.None);

        (await act.Should().ThrowAsync<BitgetSyncException>()).Which.Message.Should().Contain("rate limited");
        _syncStore.Credentials[_importStore.AccountId].LastSyncSummary.Should().StartWith("Fehlgeschlagen");
        _importStore.Transactions.Should().BeEmpty();
    }

    // ---- Saldo-Abgleich ----------------------------------------------------------------------------------

    [Fact]
    public async Task Saldo_Abgleich_ohne_Abweichung_meldet_nichts()
    {
        _client.Route("/api/v2/spot/account/assets",
            "[{\"coin\":\"BTC\",\"available\":\"0.00999\",\"frozen\":\"0\",\"locked\":\"0\"}," +
            "{\"coin\":\"USDT\",\"available\":\"1000\",\"frozen\":\"0\",\"locked\":\"0\"}]");
        // Käufe: +0.01 BTC −0.00001 Gebühr = 0.00999 BTC; USDT: +1000 Einzahlung −600 Kauf = 400
        _client.Route("/api/v2/spot/account/assets",
            "[{\"coin\":\"BTC\",\"available\":\"0.00999\",\"frozen\":\"0\",\"locked\":\"0\"}," +
            "{\"coin\":\"USDT\",\"available\":\"300\",\"frozen\":\"50\",\"locked\":\"50\"}]");

        var result = await FirstSync();

        result.BalanceMismatches.Should().BeEmpty();
        result.BalanceCheckSkipped.Should().BeNull();
        _syncStore.AppliedMismatches.Should().ContainSingle().Which.Mismatches.Should().BeEmpty();
    }

    [Fact]
    public async Task Abweichungen_zwischen_Boersen_Saldo_und_Bestand_gehen_in_die_Review_Queue()
    {
        _client.Route("/api/v2/spot/account/assets",
            "[{\"coin\":\"BTC\",\"available\":\"0.5\",\"frozen\":\"0\",\"locked\":\"0\"},{\"coin\":\"USDT\",\"available\":\"400\",\"frozen\":\"0\",\"locked\":\"0\"}]");

        var result = await FirstSync();

        var mismatch = result.BalanceMismatches.Should().ContainSingle().Which;
        mismatch.Symbol.Should().Be("BTC");
        mismatch.ExchangeBalance.Should().Be(0.5m);
        mismatch.ComputedHolding.Should().Be(0.00999m);
        mismatch.Difference.Should().Be(0.49001m);
        _syncStore.AppliedMismatches.Should().ContainSingle().Which.Mismatches.Should().ContainSingle();
    }

    [Fact]
    public async Task Fehlt_ein_Wallet_Endpunkt_wird_der_Abgleich_uebersprungen_statt_falsche_Abweichungen_zu_melden()
    {
        _client.Route("/api/v2/earn/account/assets", _ => throw new BitgetApiException("40054", "earn not enabled"));

        var result = await FirstSync();

        result.BalanceCheckSkipped.Should().Contain("übersprungen");
        result.BalanceMismatches.Should().BeEmpty();
        _syncStore.AppliedMismatches.Should().BeEmpty();
        result.Import.New.Should().Be(2, "der Import ist vom Abgleich unabhängig");
    }

    [Fact]
    public async Task Salden_aller_Wallets_werden_je_Coin_summiert()
    {
        _client.Route("/api/v2/mix/account/accounts", q => q["productType"] == "USDT-FUTURES"
            ? "[{\"marginCoin\":\"USDT\",\"available\":\"60\",\"locked\":\"40\"}]" : "[]");
        _client.Route("/api/v2/earn/account/assets", "[{\"coin\":\"USDT\",\"amount\":\"100\"}]");
        _client.Route("/api/v2/account/funding-assets", "[{\"coin\":\"USDT\",\"available\":\"100\",\"frozen\":\"0\"}]");
        _client.Route("/api/v2/spot/account/assets",
            "[{\"coin\":\"BTC\",\"available\":\"0.00999\",\"frozen\":\"0\",\"locked\":\"0\"},{\"coin\":\"USDT\",\"available\":\"100\",\"frozen\":\"0\",\"locked\":\"0\"}]");

        var totals = await BitgetBalances.FetchTotalsAsync(_client, BitgetAccountType.Classic, CancellationToken.None);

        totals["USDT"].Should().Be(400m, "Spot 100 + Futures 100 + Earn 100 + Funding 100");
        totals["BTC"].Should().Be(0.00999m);
    }

    // ---- Abgleich-Logik ----------------------------------------------------------------------------------

    [Fact]
    public void Reconciler_kennt_Toleranz_und_findet_Symbole_auf_nur_einer_Seite()
    {
        var exchange = new Dictionary<string, decimal> { ["BTC"] = 1.0m, ["ETH"] = 2m, ["BGB"] = 5m };
        var computed = new Dictionary<string, decimal> { ["btc"] = 1.0000000001m, ["ETH"] = 1.5m, ["XRP"] = 3m };

        var result = BalanceReconciler.Compare(exchange, computed);

        result.Select(m => m.Symbol).Should().Equal("BGB", "ETH", "XRP");
        result.Single(m => m.Symbol == "XRP").ExchangeBalance.Should().Be(0m);
        result.Single(m => m.Symbol == "BGB").ComputedHolding.Should().Be(0m);
    }
}
