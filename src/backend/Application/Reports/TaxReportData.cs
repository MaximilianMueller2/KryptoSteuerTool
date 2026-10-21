using KryptoSteuer.Application.Tax;
using KryptoSteuer.TaxEngine.Model;

namespace KryptoSteuer.Application.Reports;

public sealed record ReportCover(
    string? TaxpayerName, int Year, DateTimeOffset CreatedAtUtc, string RuleSet, string Method, bool Reform2027,
    IReadOnlyList<string> PriceSources, bool IsDraft, int OpenFindings, string InputHash);

/// <summary>Anlage SO, private Veräußerungsgeschäfte (andere Wirtschaftsgüter): Summen der steuerpflichtigen Zeilen der Einzelaufstellung (gerundet, auf den Cent gleich).</summary>
public sealed record AnlageSo23(decimal ProceedsEur, decimal CostEur, decimal FeesEur, decimal GainEur, decimal FreigrenzeEur, bool BelowFreigrenze, decimal TaxableEur);

/// <summary>Anlage SO, Einkünfte aus sonstigen Leistungen (§ 22 Nr. 3): Summen der Zuflüsse, die als Einkünfte zählen.</summary>
public sealed record AnlageSo22(decimal IncomeEur, decimal ExpensesEur, decimal NetEur, decimal FreigrenzeEur, bool BelowFreigrenze, decimal TaxableEur);

public sealed record IncomeRow(DateOnly Date, string Asset, string Account, decimal Quantity, decimal ValueEur, string Kind, string PriceSource, bool CountsAsIncome, string TransactionId);

public sealed record HoldingRow(string Account, string Asset, DateOnly Acquired, decimal Quantity, decimal CostEur, string Regime, DateOnly TaxFreeFrom);

public sealed record AuditRow(DateTimeOffset AtUtc, string Entity, string Reason, string? After);

public sealed record SourceRow(string Source, string FileName, string FileHash, string Status, DateTimeOffset? CommittedAtUtc, DateTimeOffset? From, DateTimeOffset? To);

/// <summary>Alle Inhalte des Jahresberichts; die Renderer (PDF, XLSX, CSV) formatieren nur noch.</summary>
public sealed record TaxReportData(
    ReportCover Cover,
    AnlageSo23 So23,
    AnlageSo22 So22,
    FuturesSummary Futures,
    DisposalReport Disposals,
    IReadOnlyList<IncomeRow> Incomes,
    IReadOnlyList<HoldingRow> Holdings,
    IReadOnlyList<AuditRow> Audit,
    IReadOnlyList<SourceRow> Sources,
    IReadOnlyList<string> OpenFindings);
