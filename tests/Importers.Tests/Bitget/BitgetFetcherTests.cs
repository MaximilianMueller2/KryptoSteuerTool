using System.Globalization;
using System.Text.Json;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Importers.Bitget;
using Microsoft.Extensions.Time.Testing;

namespace KryptoSteuer.Importers.Tests.Bitget;

public class BitgetFetcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Now);

    private BitgetFetcher Fetcher(FakeBitgetClient client) => new(client, _clock);

    private static string Items(int count, Func<int, string> item) =>
        "[" + string.Join(",", Enumerable.Range(1, count).Select(item)) + "]";

    private static FakeBitgetClient ClassicClient(Action<FakeBitgetClient>? configure = null)
    {
        var client = new FakeBitgetClient()
            .Route("/api/v3/account/info", _ => throw new BitgetApiException("40084", "not a UTA account"))
            .Route("/api/v2/spot/account/info", "{\"userId\":\"1\",\"authorities\":[\"readonly\"]}")
            .Route("/api/v2/spot/public/symbols", "[{\"symbol\":\"BTCUSDT\",\"baseCoin\":\"BTC\",\"quoteCoin\":\"USDT\"}]")
            .Route("/api/v2/spot/trade/history-orders", "[]")
            .Route("/api/v2/spot/trade/fills", "[]")
            .Route("/api/v2/spot/account/bills", "[]")
            .Route("/api/v2/spot/wallet/deposit-records", "[]")
            .Route("/api/v2/spot/wallet/withdrawal-records", "[]")
            .Route("/api/v2/convert/convert-record", "{\"dataList\":[],\"endId\":\"\"}")
            .Route("/api/v2/mix/account/bill", "{\"bills\":[],\"endId\":\"\"}")
            .Route("/api/v2/earn/savings/records", "{\"resultList\":[],\"endId\":\"\"}");
        configure?.Invoke(client);
        return client;
    }

    private static FakeBitgetClient UtaClient(Action<FakeBitgetClient>? configure = null)
    {
        var client = new FakeBitgetClient()
            .Route("/api/v3/account/info", "{\"userId\":\"1\",\"permType\":\"read_only\",\"permissions\":[]}")
            .Route("/api/v3/account/settings", "{\"accountMode\":\"unified\"}")
            .Route("/api/v3/market/instruments", "[{\"symbol\":\"BTCUSDT\",\"baseCoin\":\"BTC\",\"quoteCoin\":\"USDT\"}]")
            .Route("/api/v3/trade/fills", "{\"list\":[],\"cursor\":\"\"}")
            .Route("/api/v3/account/financial-records", "{\"list\":[],\"cursor\":\"\"}")
            .Route("/api/v3/account/deposit-records", "[]")
            .Route("/api/v3/account/withdrawal-records", "[]")
            .Route("/api/v3/account/convert-records", "{\"list\":[],\"cursor\":\"\"}");
        configure?.Invoke(client);
        return client;
    }

    // ---- Kontotyp und Rechte -----------------------------------------------------------------------------

    [Fact]
    public async Task Erkennt_ein_UTA_Konto_wenn_beide_V3_Konto_Aufrufe_gelingen()
    {
        var probe = await Fetcher(UtaClient()).ProbeAsync(CancellationToken.None);

        probe.AccountType.Should().Be(BitgetAccountType.Unified);
        probe.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Faellt_ohne_V3_Zugriff_auf_das_klassische_Konto_zurueck()
    {
        var probe = await Fetcher(ClassicClient()).ProbeAsync(CancellationToken.None);

        probe.AccountType.Should().Be(BitgetAccountType.Classic);
        probe.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("uta_trade")]
    [InlineData("withdraw")]
    public async Task Warnt_bei_Handels_oder_Auszahlungsrechten_im_UTA_Konto(string permission)
    {
        var client = UtaClient(c => c.Route("/api/v3/account/info",
            $"{{\"userId\":\"1\",\"permType\":\"read_and_write\",\"permissions\":[\"{permission}\"]}}"));

        var probe = await Fetcher(client).ProbeAsync(CancellationToken.None);

        probe.Warnings.Should().ContainSingle().Which.Should().Contain("Leserechten");
    }

    [Theory]
    [InlineData("spot_trade")]
    [InlineData("withdraw")]
    [InlineData("transfer")]
    public async Task Warnt_bei_Handels_Auszahlungs_oder_Transferrechten_im_klassischen_Konto(string authority)
    {
        var client = ClassicClient(c => c.Route("/api/v2/spot/account/info", $"{{\"authorities\":[\"readonly\",\"{authority}\"]}}"));

        var probe = await Fetcher(client).ProbeAsync(CancellationToken.None);

        probe.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task Ungueltige_Zugangsdaten_erreichen_den_Aufrufer()
    {
        var client = new FakeBitgetClient()
            .Route("/api/v3/account/info", _ => throw new BitgetApiException("40012", "apikey/password is incorrect"))
            .Route("/api/v2/spot/account/info", _ => throw new BitgetApiException("40012", "apikey/password is incorrect"));

        var act = () => Fetcher(client).ProbeAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<BitgetApiException>()).Which.Code.Should().Be("40012");
    }

    // ---- Abruf klassisches Konto -------------------------------------------------------------------------

    [Fact]
    public async Task Klassisch_fragt_nur_lesend_in_30_Tage_Fenstern_innerhalb_von_90_Tagen_ab()
    {
        var client = ClassicClient();

        await Fetcher(client).FetchAsync(Now.AddDays(-365), Now, CancellationToken.None);

        var bills = client.CallsTo("/api/v2/spot/account/bills").ToList();
        bills.Should().HaveCount(3);
        bills.Should().OnlyContain(c => c.Signed && c.Query["limit"] == "100");
        var starts = bills.Select(c => long.Parse(c.Query["startTime"], CultureInfo.InvariantCulture)).ToList();
        var ends = bills.Select(c => long.Parse(c.Query["endTime"], CultureInfo.InvariantCulture)).ToList();
        starts[0].Should().Be(Now.AddDays(-90).ToUnixTimeMilliseconds());
        ends[^1].Should().Be(Now.ToUnixTimeMilliseconds());
        ends.Zip(starts, (e, s) => e - s).Should().OnlyContain(d => d <= TimeSpan.FromDays(30).TotalMilliseconds);
        client.Calls.Where(c => c.Path.Contains("/public/")).Should().OnlyContain(c => !c.Signed);
    }

    [Fact]
    public async Task Klassisch_ermittelt_Symbole_aus_der_Orderhistorie_und_holt_Fills_je_Symbol()
    {
        var client = ClassicClient(c => c
            .Route("/api/v2/spot/trade/history-orders", "[{\"orderId\":\"o1\",\"symbol\":\"BTCUSDT\"},{\"orderId\":\"o2\",\"symbol\":\"BTCUSDT\"}]")
            .Route("/api/v2/spot/trade/fills", q => q["symbol"] == "BTCUSDT" ? "[{\"tradeId\":\"t1\",\"symbol\":\"BTCUSDT\"}]" : "[]"));

        var result = await Fetcher(client).FetchAsync(Now.AddDays(-10), Now, CancellationToken.None);

        client.CallsTo("/api/v2/spot/trade/fills").Should().ContainSingle().Which.Query["symbol"].Should().Be("BTCUSDT");
        var bundle = JsonDocument.Parse(result.BundleJson).RootElement;
        bundle.GetProperty("format").GetString().Should().Be("bitget-api-bundle/1");
        bundle.GetProperty("accountType").GetString().Should().Be("classic");
        bundle.GetProperty("symbols").GetArrayLength().Should().Be(1);
        var fills = bundle.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "spot-fills");
        fills.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Klassisch_blaettert_ueber_Cursor_idLessThan_bis_die_Seite_nicht_mehr_voll_ist()
    {
        var calls = 0;
        var client = ClassicClient(c => c.Route("/api/v2/spot/account/bills", q =>
        {
            calls++;
            if (!q.ContainsKey("idLessThan")) return Items(100, i => $"{{\"billId\":\"{1000 - i}\",\"businessType\":\"rebate rewards\"}}");
            q["idLessThan"].Should().Be("900", "der Cursor ist die billId des letzten Eintrags der Vorseite");
            return Items(20, i => $"{{\"billId\":\"{899 - i}\",\"businessType\":\"rebate rewards\"}}");
        }));

        var result = await Fetcher(client).FetchAsync(Now.AddDays(-5), Now, CancellationToken.None);

        calls.Should().Be(2);
        var bills = JsonDocument.Parse(result.BundleJson).RootElement.GetProperty("sections").EnumerateArray()
            .Single(s => s.GetProperty("kind").GetString() == "spot-bills");
        bills.GetProperty("items").GetArrayLength().Should().Be(120);
    }

    [Fact]
    public async Task Klassisch_nutzt_endId_aus_der_Antwort_als_Cursor()
    {
        var client = ClassicClient(c => c.Route("/api/v2/mix/account/bill", q =>
        {
            if (q["productType"] != "USDT-FUTURES") return "{\"bills\":[],\"endId\":\"\"}";
            return q.ContainsKey("idLessThan")
                ? "{\"bills\":[{\"billId\":\"1\"}],\"endId\":\"1\"}"
                : $"{{\"bills\":{Items(100, i => $"{{\"billId\":\"{200 - i}\"}}")},\"endId\":\"101\"}}";
        }));

        var result = await Fetcher(client).FetchAsync(Now.AddDays(-5), Now, CancellationToken.None);

        client.CallsTo("/api/v2/mix/account/bill").Where(x => x.Query["productType"] == "USDT-FUTURES").Should().HaveCount(2);
        var section = JsonDocument.Parse(result.BundleJson).RootElement.GetProperty("sections").EnumerateArray()
            .Single(s => s.GetProperty("kind").GetString() == "futures-bills");
        section.GetProperty("items").GetArrayLength().Should().Be(101);
        section.GetProperty("items")[0].GetProperty("productType").GetString().Should().Be("USDT-FUTURES");
    }

    [Fact]
    public async Task Ein_ausfallender_Endpunkt_bricht_den_Sync_nicht_ab_wird_aber_gemeldet()
    {
        var client = ClassicClient(c => c.Route("/api/v2/earn/savings/records",
            _ => throw new BitgetApiException("40054", "earn not enabled")));

        var result = await Fetcher(client).FetchAsync(Now.AddDays(-5), Now, CancellationToken.None);

        result.FailedSections.Should().ContainSingle().Which.Should().Contain("earn-savings").And.Contain("earn not enabled");
        JsonDocument.Parse(result.BundleJson).RootElement.GetProperty("sections").EnumerateArray()
            .Select(s => s.GetProperty("kind").GetString()).Should().Contain("spot-bills").And.NotContain("earn-savings");
    }

    // ---- Abruf UTA ---------------------------------------------------------------------------------------

    [Fact]
    public async Task UTA_nutzt_V3_Endpunkte_und_Cursor_der_Antwort()
    {
        var client = UtaClient(c => c.Route("/api/v3/trade/fills", q => q.ContainsKey("cursor")
            ? "{\"list\":[{\"execId\":\"x101\"}],\"cursor\":\"\"}"
            : $"{{\"list\":{Items(100, i => $"{{\"execId\":\"x{i}\"}}")},\"cursor\":\"c1\"}}"));

        var result = await Fetcher(client).FetchAsync(Now.AddDays(-5), Now, CancellationToken.None);

        result.AccountType.Should().Be(BitgetAccountType.Unified);
        client.CallsTo("/api/v3/trade/fills").Should().HaveCount(2);
        client.CallsTo("/api/v3/trade/fills").Should().OnlyContain(c => c.Query["category"] == "SPOT");
        var fills = JsonDocument.Parse(result.BundleJson).RootElement.GetProperty("sections").EnumerateArray()
            .Single(s => s.GetProperty("kind").GetString() == "uta-fills");
        fills.GetProperty("items").GetArrayLength().Should().Be(101);
        client.Calls.Select(c => c.Path).Should().NotContain(p => p.StartsWith("/api/v2/spot", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ohne_Cursor_wird_das_Zeitfenster_halbiert_bis_keine_Seite_mehr_voll_ist()
    {
        // Jede Anfrage mit einem Fenster über 1 Tag liefert eine volle Seite; kleinere Fenster liefern 10 Einträge.
        var client = UtaClient(c => c.Route("/api/v3/account/deposit-records", q =>
        {
            var span = long.Parse(q["endTime"], CultureInfo.InvariantCulture) - long.Parse(q["startTime"], CultureInfo.InvariantCulture);
            var count = span > TimeSpan.FromDays(1).TotalMilliseconds ? 100 : 10;
            return Items(count, i => $"{{\"recordId\":\"{q["startTime"]}-{i}\"}}");
        }));

        var result = await Fetcher(client).FetchAsync(Now.AddDays(-30), Now, CancellationToken.None);

        var deposits = JsonDocument.Parse(result.BundleJson).RootElement.GetProperty("sections").EnumerateArray()
            .Single(s => s.GetProperty("kind").GetString() == "uta-deposits");
        deposits.GetProperty("items").GetArrayLength().Should().BeGreaterThan(100).And.Be(client.CallsTo("/api/v3/account/deposit-records")
            .Count(c => long.Parse(c.Query["endTime"], CultureInfo.InvariantCulture) - long.Parse(c.Query["startTime"], CultureInfo.InvariantCulture)
                        <= TimeSpan.FromDays(1).TotalMilliseconds) * 10);
    }

    // ---- Zusammenspiel mit dem Import-Framework ----------------------------------------------------------

    [Fact]
    public async Task Das_Ergebnis_des_Abrufs_ist_direkt_importierbar()
    {
        var client = ClassicClient(c => c
            .Route("/api/v2/spot/trade/history-orders", "[{\"orderId\":\"o1\",\"symbol\":\"BTCUSDT\"}]")
            .Route("/api/v2/spot/trade/fills",
                "[{\"tradeId\":\"t1\",\"symbol\":\"BTCUSDT\",\"side\":\"buy\",\"size\":\"0.01\",\"amount\":\"600\",\"cTime\":\"1767225600000\",\"feeDetail\":{\"feeCoin\":\"BTC\",\"totalFee\":\"-0.00001\"}}]"));
        var result = await Fetcher(client).FetchAsync(Now.AddDays(-5), Now, CancellationToken.None);

        var store = new InMemoryImportStore();
        var service = new ImportService([new BitgetApiImporter()], store, _clock);
        var run = await service.PreviewAsync(
            new ImportInput("bitget-sync.json", System.Text.Encoding.UTF8.GetBytes(result.BundleJson)), store.AccountId, null, CancellationToken.None);

        run.Source.Should().Be("bitget-api");
        run.Report.New.Should().Be(1);
        run.Report.Errors.Should().BeEmpty();
    }
}
