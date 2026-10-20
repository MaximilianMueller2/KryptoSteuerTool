using KryptoSteuer.Application.Accounts;
using KryptoSteuer.Application.Tax;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Model;
using KryptoSteuer.TaxEngine.Time;

namespace KryptoSteuer.Application.Reports;

/// <summary>Port: Daten für den Bericht, die nicht aus der Steuer-Engine kommen.</summary>
public interface IReportStore
{
    Task<IReadOnlyList<AuditLogEntry>> GetAuditAsync(CancellationToken cancellationToken);

    /// <summary>Importläufe ohne Dateiinhalt.</summary>
    Task<IReadOnlyList<ImportRunSummary>> GetImportRunsAsync(CancellationToken cancellationToken);
}

public sealed record ImportRunSummary(string Source, string FileName, string FileHash, ImportRunStatus Status, DateTimeOffset? CommittedAtUtc);

/// <summary>Stellt den Jahresbericht zusammen (P25). Berechnung nur bis zum Jahresende, damit der Bestand dem 31.12. entspricht.</summary>
public sealed class ReportService(TaxRunService taxRuns, IReportStore store, IAccountRepository accounts, TimeProvider clock)
{
    private static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public async Task<TaxReportData> BuildAsync(TaxRunRequest request, string? taxpayerName, CancellationToken cancellationToken)
    {
        var computed = await taxRuns.ComputeAsync(request, cancellationToken, throughYearEnd: true);
        var year = request.Year;
        var assets = computed.Input.Assets;
        var accountNames = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id, a => a.Name);
        string Acc(Guid id) => accountNames.GetValueOrDefault(id, id.ToString("N")[..8]);
        string Sym(Guid id) => assets.TryGetValue(id, out var a) ? a.Symbol : id.ToString("N")[..8];

        var disposals = DisposalReport.Build(year, computed.Result, assets, accountNames);
        var taxableRows = disposals.Rows.Where(r => r.Taxable).ToList();
        var s23 = computed.Summary.Section23;
        var so23 = new AnlageSo23(
            taxableRows.Sum(r => r.ProceedsEur), taxableRows.Sum(r => r.CostEur), taxableRows.Sum(r => r.FeeEur), taxableRows.Sum(r => r.GainEur),
            s23.FreigrenzeEur, s23.BelowFreigrenze, R(s23.TaxableEur));

        var incomes = computed.Result.Incomes.Where(i => i.TaxYear == year)
            .OrderBy(i => i.TimestampUtc).ThenBy(i => i.TransactionExternalId, StringComparer.Ordinal)
            .Select(i => new IncomeRow(BerlinTime.DateOf(i.TimestampUtc), Sym(i.AssetId), Acc(i.AccountId), i.Quantity, R(i.ValueEur), i.Kind.ToString(),
                i.PriceSource, i.CountsAsIncome, i.TransactionExternalId ?? i.TransactionId.ToString("N")[..8]))
            .ToList();
        var counted = incomes.Where(i => i.CountsAsIncome).Sum(i => i.ValueEur);
        var s22 = computed.Summary.Section22;
        var so22 = new AnlageSo22(counted, R(s22.ExpensesEur), counted - R(s22.ExpensesEur), s22.FreigrenzeEur, s22.BelowFreigrenze, R(s22.TaxableEur));

        var holdings = computed.Result.Lots.Where(l => l.RemainingQuantity > 0)
            .OrderBy(l => Acc(l.AccountId), StringComparer.Ordinal).ThenBy(l => Sym(l.AssetId), StringComparer.Ordinal).ThenBy(l => l.AcquiredAtUtc)
            .Select(l => new HoldingRow(Acc(l.AccountId), Sym(l.AssetId), l.AcquiredBerlinDate, l.RemainingQuantity, R(l.RemainingCostEur), l.Regime.ToString(), l.TaxFreeFrom))
            .ToList();

        var audit = (await store.GetAuditAsync(cancellationToken))
            .OrderBy(a => a.TimestampUtc)
            .Select(a => new AuditRow(a.TimestampUtc, a.EntityType, a.Reason, a.AfterJson))
            .ToList();

        var periods = computed.Input.Transactions.GroupBy(t => t.Source)
            .ToDictionary(g => g.Key, g => (From: g.Min(t => t.TimestampUtc), To: g.Max(t => t.TimestampUtc)));
        var sources = (await store.GetImportRunsAsync(cancellationToken))
            .Where(r => r.Status == ImportRunStatus.Committed)
            .OrderBy(r => r.CommittedAtUtc)
            .Select(r => new SourceRow(r.Source, r.FileName, r.FileHash, r.Status.ToString(), r.CommittedAtUtc,
                periods.TryGetValue(r.Source, out var p) ? p.From : null, periods.TryGetValue(r.Source, out var q) ? q.To : null))
            .ToList();

        var priceSources = computed.Result.Disposals.Select(d => d.PriceSource).Concat(computed.Result.Incomes.Select(i => i.PriceSource))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var findings = computed.Result.Findings.Select(f => f.Message).ToList();

        var cover = new ReportCover(
            string.IsNullOrWhiteSpace(taxpayerName) ? null : taxpayerName.Trim(), year, clock.GetUtcNow(), $"{computed.Input.Law.Id}@{computed.Input.Law.Version}",
            request.Method.ToString(), request.ApplyReform2027, priceSources, findings.Count > 0, findings.Count, computed.InputHash);

        return new TaxReportData(cover, so23, so22, computed.Summary.Futures, disposals, incomes, holdings, audit, sources, findings);
    }
}
