using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Pricing;
using KryptoSteuer.TaxEngine.Rules;

namespace KryptoSteuer.TaxEngine.Model;

/// <summary>Eingabe der Engine: Buchungen, Assets, Kurse, Regelsätze und Einstellungen. Die Engine kennt keine Datenbank.</summary>
public sealed record TaxEngineInput(
    IReadOnlyList<Transaction> Transactions,
    IReadOnlyDictionary<Guid, Asset> Assets,
    IPriceBook Prices,
    RuleSet Law,
    RuleSet Reform,
    TaxSettings Settings)
{
    /// <summary>Entscheidungen des Nutzers aus der Review-Queue.</summary>
    public EngineDecisions Decisions { get; init; } = EngineDecisions.None;

    public static TaxEngineInput Create(
        IReadOnlyList<Transaction> transactions, IReadOnlyCollection<Asset> assets, IPriceBook? prices = null, TaxSettings? settings = null) =>
        new(transactions, assets.ToDictionary(a => a.Id), prices ?? new InMemoryPriceBook(),
            RuleSetCatalog.DeEStG23, RuleSetCatalog.DeReform2027, settings ?? new TaxSettings());
}

/// <summary>
/// Entscheidungen des Nutzers: <see cref="OwnAccountTransfers"/> ordnet eine Auszahlung ohne Gegenstück einem eigenen Zielkonto zu
/// (Auszahlungs-ID zu Konto-ID, steuerneutral, Lots wandern mit); <see cref="Acknowledged"/> sind geprüfte Buchungen ohne Wirkung auf die Berechnung.
/// </summary>
public sealed record EngineDecisions(IReadOnlyDictionary<Guid, Guid> OwnAccountTransfers, IReadOnlySet<Guid> Acknowledged)
{
    public static readonly EngineDecisions None = new(new Dictionary<Guid, Guid>(), new HashSet<Guid>());
}

public enum LotKind { Purchase, Swap, Income, Airdrop, Transfer }

/// <summary>Anschaffungslos mit Restmenge. Beträge in EUR, ungerundet.</summary>
public sealed record LotRecord(
    Guid Id, int Sequence, Guid AccountId, Guid AssetId, DateTimeOffset AcquiredAtUtc, DateOnly AcquiredBerlinDate,
    decimal Quantity, decimal RemainingQuantity, decimal CostEur, decimal RemainingCostEur,
    TaxRegime Regime, DateOnly TaxFreeFrom, LotKind Kind, Guid SourceTransactionId, string? SourceExternalId);

public enum TaxTreatment
{
    /// <summary>Privates Veräußerungsgeschäft innerhalb der Haltefrist (§ 23 EStG): steuerpflichtig.</summary>
    Section23Taxable,
    /// <summary>Haltefrist abgelaufen: steuerfrei, wird aber aufgelistet.</summary>
    TaxFreeAfterHoldingPeriod,
    /// <summary>Reform-Regelsatz (Neubestand): Einkünfte aus Kapitalvermögen.</summary>
    ReformCapital,
    /// <summary>Netzwerkgebühr bei eigenem Transfer, steuerlich neutral (Regelsatz-Option a).</summary>
    NetworkFeeNeutral,
}

public sealed record DisposalRecord(
    Guid Id, Guid TransactionId, string? TransactionExternalId, DateTimeOffset TimestampUtc, int TaxYear,
    Guid AccountId, Guid AssetId, Guid LotId, decimal Quantity,
    decimal ProceedsEur, decimal CostEur, decimal FeeEur, decimal GainEur,
    DateTimeOffset AcquiredAtUtc, int HoldingDays, DateOnly TaxFreeFrom, bool WithinHoldingPeriod,
    TaxRegime Regime, TaxTreatment Treatment, string PriceSource, string? Note);

public enum IncomeKind { Staking, Lending, Interest, Bonus, Mining, AirdropWithConsideration, AirdropWithoutConsideration }

/// <summary>Einkünfte nach § 22 Nr. 3 EStG (bzw. Kapitalvermögen unter dem Reform-Regelsatz): Marktwert im Zuflusszeitpunkt.</summary>
public sealed record IncomeRecord(
    Guid TransactionId, string? TransactionExternalId, DateTimeOffset TimestampUtc, int TaxYear,
    Guid AccountId, Guid AssetId, decimal Quantity, decimal ValueEur, IncomeKind Kind, string PriceSource, bool CountsAsIncome,
    TaxRegime Regime);

public enum FuturesKind { RealizedPnl, FundingFee, Liquidation, TradingFee }

/// <summary>Einzelne Futures-Position der Buchungen, umgerechnet in EUR (Termingeschäfte, Anlage KAP; nie mit Spot-Lots vermischt).</summary>
public sealed record FuturesRecord(
    Guid TransactionId, string? TransactionExternalId, DateTimeOffset TimestampUtc, int TaxYear, Guid AccountId, Guid AssetId,
    FuturesKind Kind, decimal AmountInAsset, decimal AmountEur, decimal FeeEur, string PriceSource, string? Note);

public sealed record TransferRecord(
    Guid OutTransactionId, Guid InTransactionId, Guid AssetId, Guid FromAccountId, Guid ToAccountId,
    decimal OutQuantity, decimal InQuantity, decimal NetworkFeeQuantity, decimal Confidence);

/// <summary>Netzwerkgebühr eines eigenen Transfers; je nach Regelsatz-Option neutral (a) oder Veräußerung (b).</summary>
public sealed record NetworkFeeRecord(
    Guid TransactionId, DateTimeOffset TimestampUtc, int TaxYear, Guid AssetId, decimal Quantity, decimal? ValueEur, TaxTreatment Treatment);

public enum FindingKind
{
    MissingPrice, NegativeHolding, UnmatchedTransferOut, UnmatchedTransferIn, TransactionNeedsReview,
    UnsupportedAsset, DonationNeedsData, MiningHint, AssetBlocked, Other,
}

/// <summary>Etwas, das die Engine nicht sicher berechnen kann (Review-Queue). Es wird nie stillschweigend 0 EUR angenommen.</summary>
public sealed record ReviewFinding(
    FindingKind Kind, string Message, Guid? TransactionId = null, Guid? AssetId = null, Guid? AccountId = null, int? TaxYear = null);

public sealed record TaxEngineResult(
    IReadOnlyList<LotRecord> Lots,
    IReadOnlyList<DisposalRecord> Disposals,
    IReadOnlyList<IncomeRecord> Incomes,
    IReadOnlyList<FuturesRecord> Futures,
    IReadOnlyList<TransferRecord> Transfers,
    IReadOnlyList<NetworkFeeRecord> NetworkFees,
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyDictionary<(Guid AccountId, Guid AssetId), decimal> Holdings);
