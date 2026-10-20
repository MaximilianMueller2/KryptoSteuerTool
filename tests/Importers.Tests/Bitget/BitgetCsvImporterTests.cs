using System.Globalization;
using System.Text;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Importers.Bitget;
using KryptoSteuer.Importers.GenericCsv;
using Microsoft.Extensions.Time.Testing;

namespace KryptoSteuer.Importers.Tests.Bitget;

public class BitgetCsvImporterTests
{
    private readonly InMemoryImportStore _store = new();
    private readonly ImportService _service;

    public BitgetCsvImporterTests()
    {
        _service = new ImportService(
            [new BitgetCsvImporter(), new BitgetApiImporter(), new GenericCsvImporter()], _store,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));
    }

    private Task<ImportRunInfo> Preview(string fixture, string? timeZone = null, byte[]? content = null) =>
        _service.PreviewAsync(
            new ImportInput(fixture, content ?? Fixtures.Read($"bitget/{fixture}"),
                timeZone is null ? null : new Dictionary<string, string> { ["timeZone"] = timeZone }),
            _store.AccountId, null, CancellationToken.None);

    private async Task<ImportRunInfo> Commit(string fixture, string? timeZone = null)
    {
        var run = await Preview(fixture, timeZone);
        return await _service.CommitAsync(run.Id, CancellationToken.None);
    }


    private decimal Holding(string symbol)
    {
        var id = _store.Assets.Single(a => a.Symbol == symbol).Id;
        return _store.Transactions.Where(t => t.Status != TransactionStatus.Ignored).SelectMany(t => t.Legs)
            .Where(l => l.AssetId == id).Sum(l => l.Direction == LegDirection.In ? l.Amount : -l.Amount);
    }

    // ---- echte Zeilen vom Anfang eines Exports ----------------------------------------------------------

    [Fact]
    public async Task Erkennt_das_Format_an_der_Kopfzeile_und_waehlt_den_richtigen_Importer()
    {
        var csv = new ImportInput("x.csv", Fixtures.Read("bitget/futures-usdtm-start.csv"));

        new BitgetCsvImporter().CanHandle(csv).Should().BeTrue();
        new GenericCsvImporter().CanHandle(csv).Should().BeFalse();
        new BitgetApiImporter().CanHandle(csv).Should().BeFalse();
        new BitgetCsvImporter().CanHandle(new ImportInput("g.csv", Fixtures.Read("generic-csv/valid.csv"))).Should().BeFalse();

        (await Preview("futures-usdtm-start.csv")).Source.Should().Be("bitget-csv");
    }

    [Fact]
    public async Task Echte_Datei_wird_ohne_Fehler_gelesen_Zeitzone_erkannt_Kontostand_lueckenlos()
    {
        var run = await Preview("futures-usdtm-start.csv");

        run.Report.RowsRead.Should().Be(100);
        run.Report.New.Should().Be(100);
        run.Report.Errors.Should().BeEmpty();
        run.Report.NeedsReview.Should().Be(0);
        run.Report.UnknownTypes.Should().BeEmpty();
        run.Report.Warnings.Should().ContainSingle().Which.Should().Contain("UTC+02:00").And.Contain("abgeleitet");
    }

    [Fact]
    public async Task Echte_Datei_Buchungsarten_Zeitstempel_und_Bestand_stimmen_mit_der_Datei_ueberein()
    {
        await Commit("futures-usdtm-start.csv");

        _store.Transactions.GroupBy(t => t.Type).ToDictionary(g => g.Key, g => g.Count()).Should().BeEquivalentTo(
            new Dictionary<TransactionType, int>
            {
                [TransactionType.Fee] = 58,           // open_long
                [TransactionType.FuturesPnL] = 13,    // close_long
                [TransactionType.FundingFee] = 18,    // contract_margin_settle_fee
                [TransactionType.Liquidation] = 4,    // 2x burst_close_long, 2x risk_captital_user_transfer
                [TransactionType.Unknown] = 7,        // trans_from_exchange (Ignored)
            });
        _store.Transactions.Where(t => t.Type == TransactionType.Unknown).Should().OnlyContain(t => t.Status == TransactionStatus.Ignored);

        // Zeitzone: 2024-11-12 17:27:25 in UTC+2 ist 15:27:25 UTC.
        _store.Transactions.Min(t => t.TimestampUtc).Should().Be(new DateTimeOffset(2024, 11, 12, 15, 27, 25, TimeSpan.Zero));
        _store.Transactions.Should().OnlyContain(t => t.TimestampUtc.Offset == TimeSpan.Zero && t.Validate().Count == 0);

        // Unabhängige Gegenprobe: Endkontostand der Datei minus Transfers vom Spot-Konto = Bestand aus den Buchungen.
        var rows = Encoding.UTF8.GetString(Fixtures.Read("bitget/futures-usdtm-start.csv")).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split(',')).ToList();
        var finalBalance = decimal.Parse(rows[0][8], CultureInfo.InvariantCulture);
        var transfers = rows.Where(r => r[5] == "trans_from_exchange").Sum(r => decimal.Parse(r[6], CultureInfo.InvariantCulture));
        Holding("USDT").Should().Be(finalBalance - transfers);
    }

    [Fact]
    public async Task Echte_Datei_Liquidation_besteht_aus_Verlust_und_Risk_Capital_Zeile_mit_Gebuehr()
    {
        await Commit("futures-usdtm-start.csv");

        var liquidations = _store.Transactions.Where(t => t.Type == TransactionType.Liquidation).ToList();
        liquidations.Should().HaveCount(4);
        liquidations.Should().OnlyContain(t => t.Legs.Any(l => !l.IsFee && l.Direction == LegDirection.Out));
        liquidations.Count(t => t.Legs.Any(l => l.IsFee)).Should().Be(2, "nur die burst_close-Zeilen tragen eine Gebühr");
        liquidations.Should().Contain(t => t.Note!.StartsWith("risk_captital_user_transfer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Open_Zeilen_ohne_Betrag_werden_zu_reinen_Gebuehren()
    {
        await Commit("futures-usdtm-start.csv");

        var fee = _store.Transactions.First(t => t.Type == TransactionType.Fee);
        fee.Legs.Should().ContainSingle().Which.Direction.Should().Be(LegDirection.Out);
        fee.Legs[0].Amount.Should().BePositive();
        fee.Note.Should().StartWith("open_long ");
    }

    [Fact]
    public async Task Order_Nummern_ohne_Tabulator_sind_die_externe_ID_und_Rohdaten_bleiben_unveraendert()
    {
        await Commit("futures-usdtm-start.csv");

        _store.RawRecords.Should().OnlyContain(r => r.ExternalId!.StartsWith("futures:1400000000000000", StringComparison.Ordinal) && !r.ExternalId.Contains('\t'));
        _store.RawRecords.Select(r => r.ExternalId).Distinct().Should().HaveCount(100);
        _store.RawRecords.Should().OnlyContain(r => r.Payload.StartsWith("Order,Date,Coin", StringComparison.Ordinal));
        _store.RawRecords.Should().OnlyContain(r => r.LineNumber >= 2);
    }

    [Fact]
    public async Task Erneuter_Import_derselben_Datei_ergibt_nur_Duplikate()
    {
        await Commit("futures-usdtm-start.csv");

        var second = await Preview("futures-usdtm-start.csv");

        second.Report.New.Should().Be(0);
        second.Report.Duplicates.Should().Be(100);
    }

    // ---- synthetische Datei mit allen Sonderfällen -------------------------------------------------------

    [Fact]
    public async Task Gemischte_Datei_ordnet_alle_Buchungsarten_zu()
    {
        var run = await Preview("futures-usdtm-mixed.csv");
        run.Report.RowsRead.Should().Be(12);
        run.Report.NeedsReview.Should().Be(1);
        run.Report.UnknownTypes.Should().Equal("mystery_type");
        run.Report.Warnings.Should().ContainSingle().Which.Should().Contain("UTC+02:00");

        await _service.CommitAsync(run.Id, CancellationToken.None);
        var tx = _store.Transactions.ToDictionary(t => t.ExternalId!);

        tx["futures:1500000000000000001"].Status.Should().Be(TransactionStatus.Ignored);      // trans_from_exchange
        tx["futures:1500000000000000002"].Type.Should().Be(TransactionType.FundingFee);
        tx["futures:1500000000000000002"].Legs.Single().Direction.Should().Be(LegDirection.In);
        tx["futures:1500000000000000003"].Type.Should().Be(TransactionType.Fee);             // open_short
        tx["futures:1500000000000000003"].Legs.Single().Amount.Should().Be(0.6m);
        tx["futures:1500000000000000004"].Status.Should().Be(TransactionStatus.Ignored);      // adjust_down_leverage
        tx["futures:1500000000000000004"].Note.Should().Contain("intern");

        var pnl = tx["futures:1500000000000000005"];                                         // close_short
        pnl.Type.Should().Be(TransactionType.FuturesPnL);
        pnl.Legs.Single(l => !l.IsFee).Amount.Should().Be(12.25m);
        pnl.Legs.Single(l => !l.IsFee).Direction.Should().Be(LegDirection.In);
        pnl.Legs.Single(l => l.IsFee).Amount.Should().Be(0.6m);

        var funding = tx["futures:1500000000000000006"];
        funding.Legs.Single().Direction.Should().Be(LegDirection.Out);
        funding.Legs.Single().Amount.Should().Be(0.2m);

        tx["futures:1500000000000000008"].Type.Should().Be(TransactionType.Bonus);            // user_grants_issue
        tx["futures:1500000000000000008"].Legs.Single().Amount.Should().Be(2m);
        tx["futures:1500000000000000009"].Status.Should().Be(TransactionStatus.Ignored);      // adjust_margin_ifm

        var mystery = tx["futures:1500000000000000010"];
        mystery.Status.Should().Be(TransactionStatus.NeedsReview);
        mystery.Type.Should().Be(TransactionType.Unknown);
        mystery.Note.Should().Contain("mystery_type");
        mystery.RawRecordIds.Should().ContainSingle();

        var burst = tx["futures:1500000000000000012"];
        burst.Type.Should().Be(TransactionType.Liquidation);
        burst.Legs.Single(l => !l.IsFee).Direction.Should().Be(LegDirection.Out);
        burst.Legs.Single(l => !l.IsFee).Amount.Should().Be(30m);
        burst.Legs.Single(l => l.IsFee).Amount.Should().Be(1m);

        // 07:00 UTC = 09:00 in UTC+2
        tx["futures:1500000000000000001"].TimestampUtc.Should().Be(new DateTimeOffset(2026, 3, 1, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Margin_Umbuchungen_aendern_den_Bestand_nicht_wie_die_Kontostand_Spalte_zeigt()
    {
        await Commit("futures-usdtm-mixed.csv");

        // Endkontostand der Datei 84,00 abzüglich Transfer 100 = -16,00
        Holding("USDT").Should().Be(-16m);
    }

    // ---- Zeitzone ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Option_timeZone_ueberschreibt_die_Erkennung_und_gilt_auch_bei_der_Bestaetigung()
    {
        var run = await Preview("futures-usdtm-mixed.csv", "+00:00");
        run.Report.Warnings.Should().ContainSingle().Which.Should().Contain("laut Option").And.Contain("UTC+00:00");

        await _service.CommitAsync(run.Id, CancellationToken.None);

        _store.Transactions.Single(t => t.ExternalId == "futures:1500000000000000001").TimestampUtc
            .Should().Be(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("UTC+2", 7)]
    [InlineData("+02:00", 7)]
    [InlineData("UTC", 9)]
    [InlineData("-05:00", 14)]
    public async Task Option_timeZone_akzeptiert_gaengige_Schreibweisen(string zone, int expectedHourUtc)
    {
        await Commit("futures-usdtm-mixed.csv", zone);

        _store.Transactions.Single(t => t.ExternalId == "futures:1500000000000000001").TimestampUtc.Hour.Should().Be(expectedHourUtc);
    }

    [Fact]
    public async Task Ungueltige_Zeitzone_ist_ein_Fehler_und_es_wird_nichts_importiert()
    {
        var run = await Preview("futures-usdtm-mixed.csv", "Mars");

        run.Report.New.Should().Be(0);
        run.Report.Errors.Should().ContainSingle().Which.Reason.Should().Contain("Mars");
    }

    [Fact]
    public async Task Ohne_Funding_Zeilen_und_ohne_Option_wird_nach_der_Zeitzone_gefragt()
    {
        var text = Encoding.UTF8.GetString(Fixtures.Read("bitget/futures-usdtm-mixed.csv"));
        var withoutFunding = string.Join("\r\n", text.Split("\r\n").Where(l => !l.Contains("settle_fee", StringComparison.Ordinal)));

        var run = await Preview("x.csv", content: Encoding.UTF8.GetBytes(withoutFunding));

        run.Report.New.Should().Be(0);
        run.Report.Errors.Should().ContainSingle().Which.Reason.Should().Contain("timeZone");
    }

    // ---- Kontostand-Prüfung ------------------------------------------------------------------------------

    [Fact]
    public async Task Fehlende_Zeile_wird_als_Luecke_im_Kontostand_gemeldet_der_Import_laeuft_trotzdem()
    {
        var run = await Preview("futures-usdtm-gap.csv");

        run.Report.New.Should().Be(11);
        run.Report.Errors.Should().BeEmpty();
        var gap = run.Report.Warnings.Single(w => w.Contains("lückenlos", StringComparison.Ordinal));
        gap.Should().Contain("1 Stellen").And.Contain("2026-03-01 18:00:03").And.Contain("erwartet");
    }

    [Fact]
    public async Task Datei_die_nicht_bei_Kontostand_0_beginnt_wird_als_moeglicherweise_unvollstaendig_gemeldet()
    {
        var lines = Encoding.UTF8.GetString(Fixtures.Read("bitget/futures-usdtm-mixed.csv")).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Where(l => !l.Contains("trans_from_exchange", StringComparison.Ordinal));

        var run = await Preview("x.csv", content: Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));

        run.Report.Warnings.Should().Contain(w => w.Contains("beginnt nicht bei Kontostand 0", StringComparison.Ordinal));
    }

    // ---- gleiche Buchung aus CSV und API nicht doppelt zählen ---------------------------------------------

    // API-Bündel mit denselben Futures-Buchungen wie in futures-usdtm-mixed.csv (UTC+2 → UTC) plus einer neuen.
    private static byte[] ApiBundle() => Encoding.UTF8.GetBytes("""
    {
      "format": "bitget-api-bundle/1", "accountType": "classic", "fetchedAtUtc": "2026-10-20T12:00:00+00:00",
      "symbols": [], "sections": [ { "kind": "futures-bills", "endpoint": "/api/v2/mix/account/bill", "items": [
        { "billId": "A1", "symbol": "BTCUSDT", "amount": "0.5",   "fee": "0",    "businessType": "contract_settle_fee", "coin": "USDT", "cTime": "1772352005412" },
        { "billId": "A2", "symbol": "BTCUSDT", "amount": "0",     "fee": "-0.6", "businessType": "open_short",          "coin": "USDT", "cTime": "1772355600000" },
        { "billId": "A3", "symbol": "BTCUSDT", "amount": "12.25", "fee": "-0.6", "businessType": "close_short",         "coin": "USDT", "cTime": "1772362800999" },
        { "billId": "A4", "symbol": "BTCUSDT", "amount": "-30",   "fee": "-1",   "businessType": "burst_close_short",   "coin": "USDT", "cTime": "1772442000000" },
        { "billId": "A5", "symbol": "BTCUSDT", "amount": "7.77",  "fee": "-0.1", "businessType": "close_long",          "coin": "USDT", "cTime": "1772445600100" }
      ] } ]
    }
    """);

    [Fact]
    public async Task API_Abruf_nach_dem_CSV_Import_zaehlt_dieselben_Buchungen_nicht_doppelt()
    {
        await Commit("futures-usdtm-mixed.csv");
        var before = _store.Transactions.Count;

        var run = await _service.PreviewAsync(new ImportInput("sync.json", ApiBundle()), _store.AccountId, null, CancellationToken.None);

        run.Source.Should().Be("bitget-api");
        run.Report.RowsRead.Should().Be(5);
        run.Report.Duplicates.Should().Be(4, "Funding, open_short, close_short und Liquidation sind schon aus der CSV da");
        run.Report.New.Should().Be(1);
        run.Report.Warnings.Should().Contain(w => w.Contains("anderen Quelle", StringComparison.Ordinal));

        await _service.CommitAsync(run.Id, CancellationToken.None);
        _store.Transactions.Should().HaveCount(before + 1);
        _store.Transactions.Last().Legs.First(l => !l.IsFee).Amount.Should().Be(7.77m);
    }

    [Fact]
    public async Task CSV_Import_nach_dem_API_Abruf_zaehlt_dieselben_Buchungen_ebenfalls_nicht_doppelt()
    {
        var api = await _service.PreviewAsync(new ImportInput("sync.json", ApiBundle()), _store.AccountId, null, CancellationToken.None);
        await _service.CommitAsync(api.Id, CancellationToken.None);

        var csv = await Preview("futures-usdtm-mixed.csv");

        csv.Report.Duplicates.Should().Be(4);
        csv.Report.New.Should().Be(8);
        csv.Report.Warnings.Should().Contain(w => w.Contains("anderen Quelle", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Abweichender_Betrag_oder_Zeitpunkt_gilt_nicht_als_dieselbe_Buchung()
    {
        await Commit("futures-usdtm-mixed.csv");
        var bundle = Encoding.UTF8.GetString(ApiBundle())
            .Replace("\"amount\": \"12.25\"", "\"amount\": \"12.26\"", StringComparison.Ordinal)   // anderer Betrag
            .Replace("1772442000000", "1772442010000", StringComparison.Ordinal);                   // 10 s später

        var run = await _service.PreviewAsync(new ImportInput("sync.json", Encoding.UTF8.GetBytes(bundle)), _store.AccountId, null, CancellationToken.None);

        run.Report.Duplicates.Should().Be(2, "nur Funding und open_short stimmen noch überein");
    }

    [Fact]
    public async Task Gleiche_Buchung_aus_derselben_Quelle_wird_nicht_ueber_den_Fingerabdruck_verworfen()
    {
        // Zwei verschiedene Order-Nummern, sonst identisch (gleiche Sekunde): beide sind echte Buchungen.
        var text = Encoding.UTF8.GetString(Fixtures.Read("bitget/futures-usdtm-mixed.csv")).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        var funding = lines.First(l => l.Contains("1500000000000000002,", StringComparison.Ordinal));
        lines.Insert(1, funding.Replace("1500000000000000002", "1500000000000000099", StringComparison.Ordinal));

        var run = await Preview("x.csv", content: Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));

        run.Report.New.Should().Be(13);
        run.Report.Duplicates.Should().Be(0);
    }
}
