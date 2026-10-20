using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Importers.GenericCsv;
using Microsoft.Extensions.Time.Testing;

namespace KryptoSteuer.Importers.Tests;

public class ImportServiceTests
{
    private readonly InMemoryImportStore _store = new();
    private readonly ImportService _service;

    public ImportServiceTests()
    {
        _service = new ImportService([new GenericCsvImporter()], _store,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));
    }

    private Task<ImportRunInfo> Preview(string fixture, string? source = null) =>
        _service.PreviewAsync(new ImportInput(fixture, Fixtures.Read($"generic-csv/{fixture}")),
            _store.AccountId, source, CancellationToken.None);

    [Fact]
    public async Task Vorschau_schreibt_weder_Buchungen_noch_Rohdaten_noch_Assets()
    {
        var run = await Preview("valid.csv");

        run.Status.Should().Be(ImportRunStatus.Pending);
        run.Report.RowsRead.Should().Be(6);
        run.Report.New.Should().Be(6);
        run.Report.Errors.Should().BeEmpty();
        _store.Transactions.Should().BeEmpty();
        _store.RawRecords.Should().BeEmpty();
        _store.Assets.Should().BeEmpty();
        _store.Runs.Should().ContainKey(run.Id);
    }

    [Fact]
    public async Task Der_Lauf_speichert_die_Quelldatei_roh_mit_SHA256()
    {
        var bytes = Fixtures.Read("generic-csv/valid.csv");

        var run = await Preview("valid.csv");

        var stored = _store.Runs[run.Id];
        stored.Content.Should().Equal(bytes);
        stored.FileHash.Should().Be(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
        stored.Source.Should().Be("generic-csv");
    }

    [Fact]
    public async Task Bestaetigung_schreibt_Rohdaten_und_Buchungen_mit_Verweis()
    {
        var run = await Preview("valid.csv");

        var committed = await _service.CommitAsync(run.Id, CancellationToken.None);

        committed.Status.Should().Be(ImportRunStatus.Committed);
        committed.CommittedAtUtc.Should().NotBeNull();
        _store.RawRecords.Should().HaveCount(6);
        _store.Transactions.Should().HaveCount(6);
        _store.Transactions.Should().OnlyContain(t => t.RawRecordIds.Count == 1);
        _store.Transactions.SelectMany(t => t.RawRecordIds).Should().BeEquivalentTo(_store.RawRecords.Select(r => r.Id));
        _store.RawRecords.Should().OnlyContain(r => r.ImportRunId == run.Id && r.LineNumber != null);
        _store.Assets.Select(a => a.Symbol).Should().BeEquivalentTo("EUR", "BTC", "ETH", "USDT");
    }

    [Fact]
    public async Task Normalisiert_Trade_Gebuehr_Zeitzone_und_Futures_Vorzeichen()
    {
        var run = await Preview("valid.csv");
        await _service.CommitAsync(run.Id, CancellationToken.None);

        var buy = _store.Transactions.Single(t => t.ExternalId == "t-2");
        buy.Type.Should().Be(TransactionType.Buy);
        buy.TimestampUtc.Should().Be(new DateTimeOffset(2026, 1, 11, 9, 0, 0, TimeSpan.Zero), "+01:00 wird nach UTC umgerechnet");
        buy.TimestampUtc.Offset.Should().Be(TimeSpan.Zero);
        buy.Legs.Should().HaveCount(3);
        buy.Legs.Single(l => l.IsFee).Amount.Should().Be(2.5m);
        buy.Legs.Single(l => !l.IsFee && l.Direction == LegDirection.In).Amount.Should().Be(0.05m);

        var pnl = _store.Transactions.Single(t => t.ExternalId == "t-6");
        pnl.Type.Should().Be(TransactionType.FuturesPnL);
        pnl.Legs.Single().Direction.Should().Be(LegDirection.Out);
        pnl.Legs.Single().Amount.Should().Be(12.5m);

        _store.Transactions.Single(t => t.ExternalId == "t-5").Type.Should().Be(TransactionType.TransferOut);
        _store.Assets.Single(a => a.Symbol == "USDT").Class.Should().Be(AssetClass.Stablecoin);
        _store.Assets.Single(a => a.Symbol == "EUR").Class.Should().Be(AssetClass.Fiat);
    }

    [Fact]
    public async Task Erneuter_Import_derselben_Datei_ergibt_nur_Duplikate()
    {
        await _service.CommitAsync((await Preview("valid.csv")).Id, CancellationToken.None);

        var second = await Preview("valid.csv");
        var committed = await _service.CommitAsync(second.Id, CancellationToken.None);

        second.Report.New.Should().Be(0);
        second.Report.Duplicates.Should().Be(6);
        committed.Report.New.Should().Be(0);
        _store.Transactions.Should().HaveCount(6);
        _store.RawRecords.Should().HaveCount(6);
    }

    [Fact]
    public async Task Duplikate_in_der_Datei_werden_ueber_externe_ID_und_Inhalts_Hash_erkannt()
    {
        var run = await Preview("duplicates.csv");

        run.Report.RowsRead.Should().Be(5);
        run.Report.New.Should().Be(3);
        run.Report.Duplicates.Should().Be(2);
    }

    [Fact]
    public async Task Fehlerhafte_Zeilen_landen_mit_Zeilennummer_und_Grund_im_Bericht_und_werden_nicht_geschrieben()
    {
        var run = await Preview("with-errors.csv");

        run.Report.RowsRead.Should().Be(6);
        run.Report.New.Should().Be(1);
        run.Report.Errors.Select(e => e.LineNumber).Should().Equal(3, 4, 5, 6, 7);
        run.Report.Errors.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Reason));
        run.Report.Errors.Single(e => e.LineNumber == 3).Reason.Should().Contain("Zeitstempel");
        run.Report.Errors.Single(e => e.LineNumber == 4).Reason.Should().Contain("counter_asset");
        run.Report.Errors.Single(e => e.LineNumber == 5).Reason.Should().Contain("Zahl");
        run.Report.Errors.Single(e => e.LineNumber == 6).Reason.Should().Contain("Spalten");

        await _service.CommitAsync(run.Id, CancellationToken.None);
        _store.Transactions.Should().ContainSingle().Which.ExternalId.Should().Be("e-1");
    }

    [Fact]
    public async Task Unbekannte_Typen_werden_nie_verworfen_sondern_als_NeedsReview_gespeichert()
    {
        var run = await Preview("unknown-type.csv");

        run.Report.New.Should().Be(4);
        run.Report.NeedsReview.Should().Be(3);
        run.Report.UnknownTypes.Should().BeEquivalentTo("margin_rebate", "dust_conversion");

        await _service.CommitAsync(run.Id, CancellationToken.None);

        var review = _store.Transactions.Where(t => t.Status == TransactionStatus.NeedsReview).ToList();
        review.Should().HaveCount(3);
        review.Should().OnlyContain(t => t.Type == TransactionType.Unknown);
        review.Select(t => t.Note).Should().OnlyContain(n => n!.Contains("Unbekannter Typ"));
        review.Should().OnlyContain(t => t.RawRecordIds.Count == 1);
        _store.Transactions.Single(t => t.ExternalId == "u-1").Status.Should().Be(TransactionStatus.Ok);
    }

    [Fact]
    public async Task Semikolon_BOM_und_Anfuehrungszeichen_werden_unterstuetzt()
    {
        var run = await Preview("semicolon-bom.csv");

        run.Report.Errors.Should().BeEmpty();
        run.Report.New.Should().Be(2);
    }

    [Fact]
    public async Task Bestaetigte_Laeufe_koennen_nicht_erneut_bestaetigt_oder_geloescht_werden()
    {
        var run = await Preview("valid.csv");
        await _service.CommitAsync(run.Id, CancellationToken.None);

        var again = () => _service.CommitAsync(run.Id, CancellationToken.None);
        var delete = () => _service.DeleteAsync(run.Id, CancellationToken.None);

        (await again.Should().ThrowAsync<ImportException>()).Which.Failure.Should().Be(ImportFailure.Conflict);
        (await delete.Should().ThrowAsync<ImportException>()).Which.Failure.Should().Be(ImportFailure.Conflict);
        _store.Runs.Should().ContainKey(run.Id);
    }

    [Fact]
    public async Task Unbestaetigte_Laeufe_koennen_geloescht_werden_ohne_Daten_zu_hinterlassen()
    {
        var run = await Preview("valid.csv");

        await _service.DeleteAsync(run.Id, CancellationToken.None);

        _store.Runs.Should().BeEmpty();
        _store.Transactions.Should().BeEmpty();
        _store.RawRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task Unbekannter_Lauf_und_unbekanntes_Konto_liefern_NotFound()
    {
        var get = () => _service.GetAsync(Guid.NewGuid(), CancellationToken.None);
        var wrongAccount = () => _service.PreviewAsync(
            new ImportInput("x.csv", Fixtures.Read("generic-csv/valid.csv")), Guid.NewGuid(), null, CancellationToken.None);

        (await get.Should().ThrowAsync<ImportException>()).Which.Failure.Should().Be(ImportFailure.NotFound);
        (await wrongAccount.Should().ThrowAsync<ImportException>()).Which.Failure.Should().Be(ImportFailure.NotFound);
    }

    [Fact]
    public async Task Nicht_erkannte_Dateien_und_unbekannte_Quellen_werden_abgelehnt()
    {
        var unknownFile = () => Preview("not-importable.csv");
        var unknownSource = () => Preview("valid.csv", "gibt-es-nicht");
        var empty = () => _service.PreviewAsync(new ImportInput("leer.csv", []), _store.AccountId, null, CancellationToken.None);

        (await unknownFile.Should().ThrowAsync<ImportException>()).Which.Failure.Should().Be(ImportFailure.Invalid);
        (await unknownSource.Should().ThrowAsync<ImportException>()).Which.Failure.Should().Be(ImportFailure.Invalid);
        (await empty.Should().ThrowAsync<ImportException>()).Which.Failure.Should().Be(ImportFailure.Invalid);
    }

    [Fact]
    public async Task GetAsync_liefert_den_gespeicherten_Bericht()
    {
        var run = await Preview("with-errors.csv");

        var loaded = await _service.GetAsync(run.Id, CancellationToken.None);

        loaded.Report.Should().BeEquivalentTo(run.Report);
    }
}
