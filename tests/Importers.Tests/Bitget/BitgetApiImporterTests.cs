using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Importers.Bitget;
using Microsoft.Extensions.Time.Testing;

namespace KryptoSteuer.Importers.Tests.Bitget;

public class BitgetApiImporterTests
{
    private readonly InMemoryImportStore _store = new();
    private readonly ImportService _service;

    public BitgetApiImporterTests()
    {
        _service = new ImportService([new BitgetApiImporter()], _store,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));
    }

    private Task<ImportRunInfo> Preview(string fixture) =>
        _service.PreviewAsync(new ImportInput(fixture, Fixtures.Read($"bitget/{fixture}")),
            _store.AccountId, null, CancellationToken.None);

    private async Task<Dictionary<string, Transaction>> Commit(string fixture)
    {
        var run = await Preview(fixture);
        await _service.CommitAsync(run.Id, CancellationToken.None);
        return _store.Transactions.ToDictionary(t => t.ExternalId!);
    }

    private string Symbol(Guid assetId) => _store.Assets.Single(a => a.Id == assetId).Symbol;

    [Fact]
    public async Task Erkennt_das_Buendel_ueber_das_Format_Feld_und_lehnt_anderes_ab()
    {
        var importer = new BitgetApiImporter();

        importer.CanHandle(new ImportInput("a.json", Fixtures.Read("bitget/synthetic-classic-bundle.json"))).Should().BeTrue();
        importer.CanHandle(new ImportInput("b.json", "{\"format\":\"x\"}"u8.ToArray())).Should().BeFalse();
        importer.CanHandle(new ImportInput("c.csv", "id,timestamp"u8.ToArray())).Should().BeFalse();
        importer.CanHandle(new ImportInput("d.json", "[1,2]"u8.ToArray())).Should().BeFalse();

        var run = await Preview("synthetic-classic-bundle.json");
        run.Source.Should().Be("bitget-api");
    }

    [Fact]
    public async Task Klassisches_Konto_Bericht_zaehlt_Zeilen_Fehler_und_Pruefungsfaelle()
    {
        var run = await Preview("synthetic-classic-bundle.json");

        run.Report.RowsRead.Should().Be(17, "bereits abgedeckte Bills und ausstehende Einzahlungen werden nicht gelesen");
        run.Report.New.Should().Be(16);
        run.Report.Errors.Should().ContainSingle().Which.Reason.Should().Contain("swap");
        run.Report.NeedsReview.Should().Be(2);
        run.Report.UnknownTypes.Should().Equal("some_new_type", "weird type");
        run.Report.Duplicates.Should().Be(0);
    }

    [Fact]
    public async Task Spot_Fills_werden_zu_Buy_und_Sell_mit_Gebuehr_und_UTC_Zeit()
    {
        var tx = await Commit("synthetic-classic-bundle.json");

        var buy = tx["spot-fills:1001"];
        buy.Type.Should().Be(TransactionType.Buy);
        buy.TimestampUtc.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        buy.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.In).Amount.Should().Be(0.01m);
        Symbol(buy.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.In).AssetId).Should().Be("BTC");
        buy.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.Out).Amount.Should().Be(600m);
        Symbol(buy.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.Out).AssetId).Should().Be("USDT");
        var fee = buy.Legs.Single(l => l.IsFee);
        fee.Amount.Should().Be(0.00001m, "Gebühren kommen negativ von Bitget und werden als positive Out-Menge gebucht");
        Symbol(fee.AssetId).Should().Be("BTC");

        var sell = tx["spot-fills:1002"];
        sell.Type.Should().Be(TransactionType.Sell);
        Symbol(sell.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.Out).AssetId).Should().Be("BTC");
        sell.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.In).Amount.Should().Be(310m);
        sell.Legs.Single(l => l.IsFee).Amount.Should().Be(0.31m);

        tx.Should().NotContainKey("spot-fills:1003", "die Zeile mit unbekannter Seite ist ein Fehler");
    }

    [Fact]
    public async Task Bills_Rewards_werden_Bonus_und_Airdrop_und_Abgedecktes_wird_nicht_doppelt_gebucht()
    {
        var tx = await Commit("synthetic-classic-bundle.json");

        tx["spot-bills:b1"].Type.Should().Be(TransactionType.Bonus);
        tx["spot-bills:b2"].Type.Should().Be(TransactionType.Airdrop);
        tx.Should().NotContainKey("spot-bills:b3", "Käufe stehen schon in den Fills");

        var review = tx["spot-bills:b4"];
        review.Status.Should().Be(TransactionStatus.NeedsReview);
        review.Type.Should().Be(TransactionType.Unknown);
        review.Note.Should().Contain("weird type");
        review.RawRecordIds.Should().ContainSingle();
    }

    [Fact]
    public async Task Ein_und_Auszahlungen_nur_abgeschlossene_Auszahlung_mit_Gebuehr()
    {
        var tx = await Commit("synthetic-classic-bundle.json");

        tx["deposits:d1"].Type.Should().Be(TransactionType.Deposit);
        tx["deposits:d1"].Legs.Single().Amount.Should().Be(1000m);
        tx.Should().NotContainKey("deposits:d2", "ausstehende Einzahlungen kommen beim nächsten Sync");

        var withdrawal = tx["withdrawals:w1"];
        withdrawal.Type.Should().Be(TransactionType.Withdrawal);
        withdrawal.Legs.Single(l => !l.IsFee).Amount.Should().Be(0.02m);
        withdrawal.Legs.Single(l => l.IsFee).Amount.Should().Be(0.0002m);
    }

    [Fact]
    public async Task Futures_Bills_werden_Gewinn_Funding_Liquidation_Gebuehr_und_Pruefungsfall()
    {
        var tx = await Commit("synthetic-classic-bundle.json");

        tx["futures-bills:f1"].Type.Should().Be(TransactionType.Fee);
        tx["futures-bills:f1"].Legs.Single().Amount.Should().Be(0.5m);

        var pnl = tx["futures-bills:f2"];
        pnl.Type.Should().Be(TransactionType.FuturesPnL);
        pnl.Legs.Single(l => !l.IsFee).Direction.Should().Be(LegDirection.In);
        pnl.Legs.Single(l => !l.IsFee).Amount.Should().Be(12.5m);
        pnl.Legs.Single(l => l.IsFee).Amount.Should().Be(0.5m);

        var funding = tx["futures-bills:f3"];
        funding.Type.Should().Be(TransactionType.FundingFee);
        funding.Legs.Single().Direction.Should().Be(LegDirection.Out);
        funding.Legs.Single().Amount.Should().Be(0.2m);

        tx["futures-bills:f4"].Status.Should().Be(TransactionStatus.Ignored);
        tx["futures-bills:f4"].Note.Should().Contain("intern");

        var liquidation = tx["futures-bills:f5"];
        liquidation.Type.Should().Be(TransactionType.Liquidation);
        liquidation.Legs.Single().Direction.Should().Be(LegDirection.Out);
        liquidation.Legs.Single().Amount.Should().Be(100m);

        tx["futures-bills:f6"].Status.Should().Be(TransactionStatus.NeedsReview);
    }

    [Fact]
    public async Task Convert_und_Earn_werden_zugeordnet()
    {
        var tx = await Commit("synthetic-classic-bundle.json");

        var convert = tx["convert:c1"];
        convert.Type.Should().Be(TransactionType.Trade);
        Symbol(convert.Legs.Single(l => l.Direction == LegDirection.In).AssetId).Should().Be("BGB");
        convert.Legs.Single(l => l.Direction == LegDirection.In).Amount.Should().Be(50m);
        convert.Legs.Single(l => l.Direction == LegDirection.Out).Amount.Should().Be(100m);

        var interest = tx.Values.Single(t => t.ExternalId!.StartsWith("earn-savings:e1:", StringComparison.Ordinal));
        interest.Type.Should().Be(TransactionType.Interest);
        interest.Legs.Single().Amount.Should().Be(0.12m);

        tx.Values.Single(t => t.ExternalId!.StartsWith("earn-savings:e2:", StringComparison.Ordinal))
            .Status.Should().Be(TransactionStatus.Ignored);
    }

    [Fact]
    public async Task Jede_Buchung_verweist_auf_ihr_Original_json_als_RawRecord()
    {
        await Commit("synthetic-classic-bundle.json");

        _store.RawRecords.Should().HaveCount(16);
        _store.RawRecords.Should().OnlyContain(r => r.ExternalId!.Contains(':') && r.Payload.Contains("\"item\""));
        _store.Transactions.SelectMany(t => t.RawRecordIds).Should().BeEquivalentTo(_store.RawRecords.Select(r => r.Id));
        _store.Transactions.Should().OnlyContain(t => t.TimestampUtc.Offset == TimeSpan.Zero);
        _store.Transactions.Should().OnlyContain(t => t.Validate().Count == 0);
    }

    [Fact]
    public async Task Erneuter_Abruf_derselben_Daten_ergibt_nur_Duplikate()
    {
        await Commit("synthetic-classic-bundle.json");

        var second = await Preview("synthetic-classic-bundle.json");

        second.Report.New.Should().Be(0);
        second.Report.Duplicates.Should().Be(16);
    }

    [Fact]
    public async Task UTA_Bundle_wird_ueber_V3_Felder_normalisiert()
    {
        var run = await Preview("synthetic-uta-bundle.json");
        run.Report.RowsRead.Should().Be(8);
        run.Report.Errors.Should().BeEmpty();
        run.Report.NeedsReview.Should().Be(1);
        run.Report.UnknownTypes.Should().Equal("INTEREST_SETTLEMENT_OUT");

        var tx = await Commit("synthetic-uta-bundle.json");

        var fill = tx["uta-fills:x1"];
        fill.Type.Should().Be(TransactionType.Buy);
        fill.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.In).Amount.Should().Be(0.1m);
        fill.Legs.Single(l => l.IsFee).Amount.Should().Be(6m);

        var pnl = tx["uta-financial:fin1"];
        pnl.Type.Should().Be(TransactionType.FuturesPnL);
        pnl.Legs.Single(l => !l.IsFee).Amount.Should().Be(20m);
        pnl.Legs.Single(l => l.IsFee).Amount.Should().Be(1m);

        var funding = tx["uta-financial:fin2"];
        funding.Type.Should().Be(TransactionType.FundingFee);
        funding.Legs.Single().Direction.Should().Be(LegDirection.In);

        tx["uta-financial:fin3"].Status.Should().Be(TransactionStatus.Ignored);
        tx["uta-financial:fin4"].Status.Should().Be(TransactionStatus.NeedsReview);
        tx["uta-deposits:r1"].Type.Should().Be(TransactionType.Deposit);
        tx["uta-withdrawals:r2"].Legs.Single(l => l.IsFee).Amount.Should().Be(1m);
        tx.Values.Single(t => t.ExternalId!.StartsWith("uta-convert:", StringComparison.Ordinal)).Type.Should().Be(TransactionType.Trade);
    }

    [Fact]
    public async Task Unbekanntes_Symbol_ist_ein_Zeilenfehler_und_kein_stiller_Verlust()
    {
        var json = System.Text.Encoding.UTF8.GetString(Fixtures.Read("bitget/synthetic-uta-bundle.json"))
            .Replace("\"symbol\": \"BTCUSDT\", \"baseCoin\"", "\"symbol\": \"OTHER\", \"baseCoin\"", StringComparison.Ordinal);

        var run = await _service.PreviewAsync(new ImportInput("x.json", System.Text.Encoding.UTF8.GetBytes(json)),
            _store.AccountId, null, CancellationToken.None);

        run.Report.Errors.Should().Contain(e => e.Reason.Contains("BTCUSDT"));
    }
}
