using KryptoSteuer.TaxEngine.Model;
using KryptoSteuer.TaxEngine.Rules;

namespace KryptoSteuer.Application.Tax;

/// <summary>Anfrage für einen Berechnungslauf: Steuerjahr, Einstellungen und Angaben des Nutzers.</summary>
public sealed record TaxRunRequest
{
    public required int Year { get; init; }
    public bool ApplyReform2027 { get; init; }
    public ConsumptionMethod Method { get; init; } = ConsumptionMethod.Fifo;
    public RegimeOrder RegimeOrder { get; init; } = RegimeOrder.Fifo;
    public NetworkFeeTreatment NetworkFees { get; init; } = NetworkFeeTreatment.NeutralWithoutProceeds;
    public AirdropTreatment Airdrops { get; init; } = AirdropTreatment.WithoutConsideration;
    /// <summary>Symbole, die keine Tauschkryptowerte sind.</summary>
    public IReadOnlyList<string> NotTauschkrypto { get; init; } = [];
    public decimal OtherPrivateSalesGainEur { get; init; }
    public decimal LossCarryForwardInEur { get; init; }
    public decimal Section22ExpensesEur { get; init; }
    public bool JointAssessment { get; init; }

    public TaxSettings ToSettings() => new()
    {
        ApplyReform2027 = ApplyReform2027, Method = Method, RegimeOrder = RegimeOrder, NetworkFees = NetworkFees, Airdrops = Airdrops,
        NotTauschkrypto = new HashSet<string>(NotTauschkrypto, StringComparer.OrdinalIgnoreCase),
    };

    public YearInputs ToYearInputs() => new(Year, OtherPrivateSalesGainEur, LossCarryForwardInEur, Section22ExpensesEur, JointAssessment);
}

/// <summary>Eine Änderung gegenüber der vorherigen Berechnung desselben Jahres.</summary>
public sealed record TaxRunDiffEntry(string Path, string? Before, string? After);

public sealed record TaxRunCounts(int Lots, int Disposals, int Incomes, int FuturesRecords, int Transfers, int Findings, int Transactions);

public sealed record TaxRunResult(
    Guid Id,
    int Year,
    DateTimeOffset CreatedAtUtc,
    string RuleSet,
    string InputHash,
    bool InputUnchanged,
    TaxRunCounts Counts,
    YearSummary Summary,
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<TaxRunDiffEntry>? DiffToPrevious,
    bool IsDraft);
