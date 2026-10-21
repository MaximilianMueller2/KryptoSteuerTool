using System.Globalization;
using System.Text;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Importers.Bitget;
using KryptoSteuer.Importers.GenericCsv;
using KryptoSteuer.Importers.TradeRepublic;
using Microsoft.Extensions.Time.Testing;

namespace KryptoSteuer.Importers.Tests.TradeRepublic;

public class TradeRepublicCsvImporterTests
{
    private readonly InMemoryImportStore _store = new();
    private readonly ImportService _service;

    public TradeRepublicCsvImporterTests()
    {
        _service = new ImportService(
            [new TradeRepublicCsvImporter(), new BitgetCsvImporter(), new BitgetApiImporter(), new GenericCsvImporter()], _store,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));
    }

    private Task<ImportRunInfo> Preview(string fixture) =>
        _service.PreviewAsync(new ImportInput(fixture, Fixtures.Read($"traderepublic/{fixture}")), _store.AccountId, null, CancellationToken.None);

    private async Task<ImportRunInfo> Commit(string fixture)
    {
        var run = await Preview(fixture);
        return await _service.CommitAsync(run.Id, CancellationToken.None);
    }

    private string Symbol(Guid assetId) => _store.Assets.Single(a => a.Id == assetId).Symbol;

    /// <summary>Fixture als Zeilen mit Spaltennamen, unabhängig vom Importer gelesen.</summary>
    private static List<Dictionary<string, string>> FixtureRows(string fixture)
    {
        var rows = KryptoSteuer.Importers.Common.CsvReader.Read(Fixtures.Read($"traderepublic/{fixture}")).ToList();
        var header = rows[0].Fields.Select(f => f.Trim()).ToList();
        return rows.Skip(1).Select(r => header.Zip(r.Fields, (h, v) => (h, v)).ToDictionary(x => x.h, x => x.v)).ToList();
    }

    private static decimal D(string s) => string.IsNullOrWhiteSpace(s) ? 0m : decimal.Parse(s, CultureInfo.InvariantCulture);

    // ---- Erkennung ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Erkennt_den_Export_an_der_Kopfzeile_und_nur_diesen()
    {
        var tr = new ImportInput("x.csv", Fixtures.Read("traderepublic/traderepublic-sample.csv"));

        new TradeRepublicCsvImporter().CanHandle(tr).Should().BeTrue();
        new BitgetCsvImporter().CanHandle(tr).Should().BeFalse();
        new GenericCsvImporter().CanHandle(tr).Should().BeFalse();
        new BitgetApiImporter().CanHandle(tr).Should().BeFalse();
        new TradeRepublicCsvImporter().CanHandle(new ImportInput("b.csv", Fixtures.Read("bitget/futures-usdtm-mixed.csv"))).Should().BeFalse();
        new TradeRepublicCsvImporter().CanHandle(new ImportInput("g.csv", Fixtures.Read("generic-csv/valid.csv"))).Should().BeFalse();

        (await Preview("traderepublic-sample.csv")).Source.Should().Be("traderepublic-csv");
    }

    // ---- echte Zeilen ------------------------------------------------------------------------------------

    [Fact]
    public async Task Echte_Zeilen_werden_ohne_Fehler_gelesen_und_Steuerbuchungen_zusaetzlich_erzeugt()
    {
        var rows = FixtureRows("traderepublic-sample.csv");
        var run = await Preview("traderepublic-sample.csv");

        // Zeilen mit Steuer ergeben eine zweite Buchung, außer es gibt keine Hauptbuchung (Steueroptimierung, Vorabpauschale mit Betrag 0).
        var withTax = rows.Count(r => D(r["tax"]) != 0 && r["type"] != "TAX_OPTIMIZATION" && !(r["type"] == "EARNINGS" && D(r["amount"]) == 0));
        run.Report.RowsRead.Should().Be(rows.Count);
        run.Report.Errors.Should().BeEmpty();
        run.Report.New.Should().Be(rows.Count + withTax);
        run.Report.NeedsReview.Should().Be(rows.Count(r => r["category"] == "CORPORATE_ACTION" || r["type"] == "TILG"));
        run.Report.UnknownTypes.Should().BeEquivalentTo(
            "CASH/TILG", "CORPORATE_ACTION/SPIN_OFF", "CORPORATE_ACTION/SPIN_OFF_CANCELLED", "CORPORATE_ACTION/WARRANT_EXERCISE");
        run.Report.Warnings.Should().ContainSingle().Which.Should().Contain("6 Kartenzahlungen");
    }

    [Fact]
    public async Task Echte_Zeilen_Buchungsarten_und_Statuswerte()
    {
        var rows = FixtureRows("traderepublic-sample.csv");
        await Commit("traderepublic-sample.csv");
        var count = (TransactionType t, TransactionStatus? s = null) =>
            _store.Transactions.Count(x => x.Type == t && (s is null || x.Status == s));

        count(TransactionType.Buy).Should().Be(rows.Count(r => r["type"] == "BUY"));
        count(TransactionType.Sell).Should().Be(rows.Count(r => r["type"] == "SELL"));
        count(TransactionType.Interest).Should().Be(rows.Count(r => r["type"] == "INTEREST_PAYMENT"));
        count(TransactionType.Dividend).Should().Be(rows.Count(r => r["type"] == "DIVIDEND"));
        count(TransactionType.Bonus).Should().Be(rows.Count(r => r["type"] is "STOCKPERK" or "BENEFITS_SAVEBACK"));
        count(TransactionType.GiftIn).Should().Be(rows.Count(r => r["type"] == "GIFT" && D(r["amount"]) > 0));
        count(TransactionType.GiftOut).Should().Be(rows.Count(r => r["type"] == "GIFT" && D(r["amount"]) < 0));
        count(TransactionType.Fee).Should().Be(2, "Kartengebühr und Gebühr bei der Lieferung");
        count(TransactionType.TransferOut).Should().Be(1, "FREE_DELIVERY von Krypto");
        count(TransactionType.Deposit).Should().Be(rows.Count(r => r["type"].StartsWith("CUSTOMER_IN", StringComparison.Ordinal)
            || r["type"].StartsWith("TRANSFER_", StringComparison.Ordinal) && D(r["amount"]) > 0));
        count(TransactionType.Unknown, TransactionStatus.Ignored).Should().Be(6, "nur Kartenzahlungen");
        count(TransactionType.Unknown, TransactionStatus.NeedsReview).Should().Be(9, "6 Kapitalmaßnahmen und 3 Tilgungen");
        _store.Transactions.Should().OnlyContain(t => t.Validate().Count == 0 && t.TimestampUtc.Offset == TimeSpan.Zero);
    }

    [Fact]
    public async Task Krypto_Bestand_und_Steuersummen_stimmen_mit_der_Datei_ueberein()
    {
        var rows = FixtureRows("traderepublic-sample.csv");
        await Commit("traderepublic-sample.csv");

        // Bestand je Krypto-Symbol aus den Buchungen = Summe der Stückzahlen aus der Datei
        var expected = rows.Where(r => r["asset_class"] == "CRYPTO" && r["shares"].Length > 0)
            .GroupBy(r => r["symbol"] == "XF0RENDER015" ? "RENDER" : r["symbol"])
            .ToDictionary(g => g.Key, g => g.Sum(r => D(r["shares"])));
        var cryptoIds = _store.Assets.Where(a => a.Class == AssetClass.Crypto).ToDictionary(a => a.Id, a => a.Symbol);
        var actual = _store.Transactions.SelectMany(t => t.Legs).Where(l => cryptoIds.ContainsKey(l.AssetId))
            .GroupBy(l => cryptoIds[l.AssetId])
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Direction == LegDirection.In ? l.Amount : -l.Amount));
        actual.Should().BeEquivalentTo(expected);

        var tax = _store.Transactions.Where(t => t.Type == TransactionType.Tax).SelectMany(t => t.Legs).ToList();
        tax.Where(l => l.Direction == LegDirection.In).Sum(l => l.Amount).Should().Be(rows.Select(r => D(r["tax"])).Where(x => x > 0).Sum());
        tax.Where(l => l.Direction == LegDirection.Out).Sum(l => l.Amount).Should().Be(-rows.Select(r => D(r["tax"])).Where(x => x < 0).Sum());
    }

    [Fact]
    public async Task Verkauf_mit_Gebuehr_und_einbehaltener_Steuer_wird_in_Verkauf_und_Steuerbuchung_geteilt()
    {
        await Commit("traderepublic-sample.csv");

        // Optionsschein DE000HS5QAL2: 75 Stück zu 1,89 = 141,75 EUR, Gebühr 1,00, einbehaltene Steuer 32,90
        var sell = _store.Transactions.Single(t => t.Type == TransactionType.Sell
            && t.Legs.Any(l => l.Direction == LegDirection.In && !l.IsFee && l.Amount == 141.75m));
        sell.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.Out).Amount.Should().Be(75m);
        Symbol(sell.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.Out).AssetId).Should().Be("DE000HS5QAL2");
        sell.Legs.Single(l => l.IsFee).Amount.Should().Be(1m);
        sell.Legs.Single(l => l.IsFee).Direction.Should().Be(LegDirection.Out);

        var tax = _store.Transactions.Single(t => t.ExternalId == sell.ExternalId + ":tax");
        tax.Type.Should().Be(TransactionType.Tax);
        tax.Legs.Single().Direction.Should().Be(LegDirection.Out);
        tax.Legs.Single().Amount.Should().Be(32.90m);
        tax.TimestampUtc.Should().Be(sell.TimestampUtc);
        tax.RawRecordIds.Should().Equal(sell.RawRecordIds);
        _store.Assets.Single(a => a.Symbol == "DE000HS5QAL2").Class.Should().Be(AssetClass.Derivative);
    }

    [Fact]
    public async Task Krypto_Kauf_und_Verkauf_mit_Gebuehr_Zeit_in_UTC()
    {
        await Commit("traderepublic-sample.csv");

        var buy = _store.Transactions.Where(t => t.Type == TransactionType.Buy)
            .Single(t => t.Legs.Any(l => !l.IsFee && l.Direction == LegDirection.In && Symbol(l.AssetId) == "BTC") && t.TimestampUtc.Year == 2024 && t.TimestampUtc.Month == 7);
        buy.TimestampUtc.Should().Be(new DateTimeOffset(2024, 7, 31, 10, 20, 46, 656, TimeSpan.Zero));
        buy.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.In).Amount.Should().Be(0.008108m);
        buy.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.Out).Amount.Should().Be(499.96m);
        buy.Legs.Single(l => l.IsFee).Amount.Should().Be(1m);
        Symbol(buy.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.Out).AssetId).Should().Be("EUR");
        _store.Assets.Single(a => a.Symbol == "BTC").Name.Should().Be("Bitcoin");
        _store.Assets.Single(a => a.Symbol == "EUR").Class.Should().Be(AssetClass.Fiat);
    }

    [Fact]
    public async Task Wertpapiere_bekommen_ISIN_Name_und_Klasse()
    {
        await Commit("traderepublic-sample.csv");

        var fund = _store.Assets.Single(a => a.Symbol == "IE00B4L5Y983");
        fund.Class.Should().Be(AssetClass.Etf);
        fund.Isin.Should().Be("IE00B4L5Y983");
        fund.Name.Should().Be("Core MSCI World USD (Acc)");
        _store.Assets.Single(a => a.Symbol == "US36467W1099").Class.Should().Be(AssetClass.Stock);
        _store.Assets.Where(a => a.Class == AssetClass.Crypto).Should().OnlyContain(a => a.Isin == null);
    }

    [Fact]
    public async Task Lieferung_von_Krypto_ist_eine_Uebertragung_und_kein_Verkauf()
    {
        await Commit("traderepublic-sample.csv");

        var delivery = _store.Transactions.Single(t => t.Type == TransactionType.TransferOut);
        delivery.Legs.Should().ContainSingle().Which.Direction.Should().Be(LegDirection.Out);
        Symbol(delivery.Legs[0].AssetId).Should().Be("SOL");
        delivery.Legs[0].Amount.Should().Be(1.115573m);
        _store.Transactions.Count(t => t.Type == TransactionType.Sell && t.Legs.Any(l => Symbol(l.AssetId) == "SOL"))
            .Should().Be(0, "in der Stichprobe gibt es keinen SOL-Verkauf");
    }

    [Fact]
    public async Task Saveback_und_Stockperk_sind_Bonus_Zahlungen_und_Zinsen_mit_Steuer_getrennt()
    {
        await Commit("traderepublic-sample.csv");

        var saveback = _store.Transactions.First(t => t.Type == TransactionType.Bonus && t.Note!.Contains("Saveback"));
        saveback.Legs.Single().Direction.Should().Be(LegDirection.In);
        saveback.Legs.Single().Amount.Should().Be(0.94m);

        var interest = _store.Transactions.First(t => t.Type == TransactionType.Interest);
        interest.Legs.Single().Amount.Should().Be(1.11m);
        _store.Transactions.Single(t => t.ExternalId == interest.ExternalId + ":tax").Legs.Single().Amount.Should().Be(0.29m);
    }

    [Fact]
    public async Task Kartenzahlungen_werden_ignoriert_aber_Ein_und_Auszahlungen_gebucht()
    {
        await Commit("traderepublic-sample.csv");

        _store.Transactions.Where(t => t.Note!.StartsWith("CASH/CARD", StringComparison.Ordinal))
            .Should().OnlyContain(t => t.Status == TransactionStatus.Ignored && t.Legs.Count == 0);
        _store.Transactions.Where(t => t.Type == TransactionType.Withdrawal).Should().OnlyContain(t => t.Legs.Single().Direction == LegDirection.Out);
        _store.Transactions.Where(t => t.Type == TransactionType.Deposit).Should().OnlyContain(t => t.Legs.Single().Direction == LegDirection.In);
    }

    [Fact]
    public async Task Kapitalmassnahmen_und_Tilgung_gehen_zur_Pruefung_mit_Rohzeile()
    {
        await Commit("traderepublic-sample.csv");

        var review = _store.Transactions.Where(t => t.Status == TransactionStatus.NeedsReview).ToList();
        review.Should().OnlyContain(t => t.Type == TransactionType.Unknown && t.RawRecordIds.Count == 1);
        review.Should().Contain(t => t.Note!.StartsWith("Kapitalmaßnahme WARRANT_EXERCISE", StringComparison.Ordinal)
            && t.Legs.Single().Direction == LegDirection.Out);
        review.Should().Contain(t => t.Note!.StartsWith("Tilgung", StringComparison.Ordinal) && t.Legs.Single().Direction == LegDirection.In);
    }

    [Fact]
    public async Task Rohdaten_externe_ID_und_Wiederholung()
    {
        await Commit("traderepublic-sample.csv");

        _store.RawRecords.Should().OnlyContain(r => r.ExternalId!.StartsWith("tr:", StringComparison.Ordinal) && r.LineNumber >= 2);
        _store.RawRecords.Select(r => r.ExternalId).Should().OnlyHaveUniqueItems();
        _store.Transactions.Select(t => t.ExternalId).Should().OnlyHaveUniqueItems();

        var second = await Preview("traderepublic-sample.csv");
        second.Report.New.Should().Be(0);
        second.Report.Duplicates.Should().Be(91);
    }

    [Fact]
    public async Task Persoenliche_Daten_landen_nicht_in_den_Hinweistexten()
    {
        await Commit("traderepublic-sample.csv");

        _store.Transactions.Select(t => t.Note).Should().OnlyContain(n => !n!.Contains("IBAN") && !n.Contains("Merchant") && !n.Contains("SEPA"));
    }

    // ---- Sonderfälle -------------------------------------------------------------------------------------

    [Fact]
    public async Task Sonderfaelle_Fehler_mit_Zeilennummer_unbekannte_Typen_und_Pseudo_ISIN()
    {
        var run = await Preview("traderepublic-edge-cases.csv");

        run.Report.RowsRead.Should().Be(11);
        run.Report.New.Should().Be(6);
        run.Report.NeedsReview.Should().Be(2);
        run.Report.UnknownTypes.Should().Equal("CASH/MYSTERY_BOOKING", "STAKING/REWARD");
        run.Report.Errors.Select(e => e.LineNumber).Should().Equal(4, 5, 6, 9, 10, 11);
        var reasons = run.Report.Errors.ToDictionary(e => e.LineNumber!.Value, e => e.Reason);
        reasons[4].Should().Contain("Vorzeichen");
        reasons[5].Should().Contain("USD");
        reasons[6].Should().Contain("Zeitstempel");
        reasons[9].Should().Contain("asset_class");
        reasons[10].Should().Contain("Zahl");
        reasons[11].Should().Contain("transaction_id");

        await _service.CommitAsync(run.Id, CancellationToken.None);
        var tx = _store.Transactions.ToDictionary(t => t.ExternalId!);

        // Pseudo-ISIN XF0RENDER015 wird zum Ticker RENDER
        var render = tx["tr:00000000-0000-0000-0000-000000000001"];
        Symbol(render.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.In).AssetId).Should().Be("RENDER");

        // Verkauf mit Steuer: zwei Buchungen mit unterschiedlicher externer ID
        tx["tr:00000000-0000-0000-0000-000000000002"].Type.Should().Be(TransactionType.Sell);
        tx["tr:00000000-0000-0000-0000-000000000002:tax"].Legs.Single().Amount.Should().Be(5.25m);

        tx["tr:00000000-0000-0000-0000-000000000006"].Status.Should().Be(TransactionStatus.NeedsReview);
        tx["tr:00000000-0000-0000-0000-000000000006"].Legs.Single().Direction.Should().Be(LegDirection.Out);
        tx["tr:00000000-0000-0000-0000-000000000007"].Status.Should().Be(TransactionStatus.NeedsReview);
        tx["tr:00000000-0000-0000-0000-000000000011"].Status.Should().Be(TransactionStatus.Ignored);
    }
}
