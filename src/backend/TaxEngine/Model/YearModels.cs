namespace KryptoSteuer.TaxEngine.Model;

/// <summary>Angaben des Nutzers je Steuerjahr, die nicht aus den Buchungen folgen.</summary>
/// <param name="OtherPrivateSalesGainEur">Gewinn/Verlust aus anderen privaten Veräußerungsgeschäften (z. B. Gold); die Freigrenze gilt für alle zusammen.</param>
/// <param name="LossCarryForwardInEur">Verlustvortrag § 23 aus dem Vorjahr (laut Bescheid über die gesonderte Feststellung), als positiver Betrag.</param>
/// <param name="Section22ExpensesEur">Werbungskosten zu den Einkünften nach § 22 Nr. 3 (z. B. Gebühren).</param>
/// <param name="JointAssessment">Zusammenveranlagung (Sparer-Pauschbetrag 2.000 statt 1.000 EUR im Reform-Regelsatz).</param>
public sealed record YearInputs(
    int Year,
    decimal OtherPrivateSalesGainEur = 0m,
    decimal LossCarryForwardInEur = 0m,
    decimal Section22ExpensesEur = 0m,
    bool JointAssessment = false);

public sealed record AssetResult(Guid AccountId, Guid AssetId, decimal GainsEur, decimal LossesEur, decimal NetEur, decimal ProceedsEur, decimal CostEur, decimal FeesEur);

/// <summary>Private Veräußerungsgeschäfte nach § 23 EStG im Steuerjahr.</summary>
public sealed record Section23Summary(
    int Year,
    decimal ProceedsEur, decimal CostEur, decimal FeesEur,
    decimal GainsEur, decimal LossesEur, decimal NetEur,
    decimal OtherPrivateSalesEur, decimal TotalGainEur,
    decimal FreigrenzeEur, bool BelowFreigrenze,
    decimal LossCarryInEur, decimal LossUsedEur, decimal TaxableEur, decimal LossCarryOutEur,
    decimal TaxFreeAfterPeriodNetEur,
    IReadOnlyList<AssetResult> ByAssetAndAccount);

/// <summary>Einkünfte aus sonstigen Leistungen nach § 22 Nr. 3 EStG.</summary>
public sealed record Section22Summary(
    int Year, decimal IncomeEur, decimal ExpensesEur, decimal NetEur, decimal FreigrenzeEur, bool BelowFreigrenze, decimal TaxableEur,
    IReadOnlyDictionary<IncomeKind, decimal> IncomeByKind);

/// <summary>Termingeschäfte (Bitget-Futures) in EUR, getrennt von § 23: Anlage KAP, keine Haltefrist.</summary>
public sealed record FuturesSummary(
    int Year, decimal RealizedGainsEur, decimal RealizedLossesEur, decimal FundingNetEur, decimal TradingFeesEur,
    decimal LiquidationEur, decimal NetEur, int Records);

/// <summary>Reform-Regelsatz (nur wenn eingeschaltet): Neubestand als Einkünfte aus Kapitalvermögen.</summary>
public sealed record ReformSummary(
    int Year, decimal NetCapitalGainEur, decimal PauschbetragEur, decimal TaxableEur, decimal FlatTaxEur, decimal SolidarityEur, decimal TotalTaxEur);

public sealed record YearSummary(
    int Year, string LawRuleSet, string LawVersion, string? ReformRuleSet,
    Section23Summary Section23, Section22Summary Section22, FuturesSummary Futures, ReformSummary? Reform,
    IReadOnlyList<ReviewFinding> OpenFindings);

public sealed record NetworkFeeSummary(int Year, IReadOnlyDictionary<Guid, decimal> QuantityByAsset, int Transfers);
