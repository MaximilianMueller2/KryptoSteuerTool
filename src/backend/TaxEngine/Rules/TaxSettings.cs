namespace KryptoSteuer.TaxEngine.Rules;

public enum ConsumptionMethod { Fifo, AverageCost }

/// <summary>Reihenfolge von Alt- und Neu-Lots im selben Konto (der Entwurf regelt das nicht abschließend).</summary>
public enum RegimeOrder { Fifo, NeuFirst }

public enum NetworkFeeTreatment
{
    /// <summary>(a) Abgang ohne Erlös, steuerlich neutral markiert, im Report gesondert ausgewiesen (Standard).</summary>
    NeutralWithoutProceeds,
    /// <summary>(b) Abgang mit Erlös in Höhe des Marktwerts (Veräußerung).</summary>
    DisposalAtMarketValue,
}

public enum AirdropTreatment
{
    /// <summary>Airdrop ohne Gegenleistung: Einnahme 0, Anschaffungskosten 0 (Frist beginnt mit dem Zufluss).</summary>
    WithoutConsideration,
    /// <summary>Airdrop mit Gegenleistung: Einnahme nach § 22 Nr. 3 in Höhe des Marktwerts, zugleich Anschaffung.</summary>
    WithConsideration,
}

/// <summary>Einstellungen eines Berechnungslaufs. Werden zusammen mit dem Regelsatz im TaxRun gespeichert.</summary>
public sealed record TaxSettings
{
    /// <summary>"Reform 2027 anwenden" (Standard: aus). Solange aus, rechnet alles nach § 23; die Lots tragen trotzdem ihr Regime.</summary>
    public bool ApplyReform2027 { get; init; }

    public ConsumptionMethod Method { get; init; } = ConsumptionMethod.Fifo;
    public RegimeOrder RegimeOrder { get; init; } = RegimeOrder.Fifo;
    public NetworkFeeTreatment NetworkFees { get; init; } = NetworkFeeTreatment.NeutralWithoutProceeds;
    public AirdropTreatment Airdrops { get; init; } = AirdropTreatment.WithoutConsideration;

    /// <summary>Symbole, die keine Tauschkryptowerte sind (z. B. NFTs oder ausgenommene Stablecoins).</summary>
    public IReadOnlySet<string> NotTauschkrypto { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Asset-Paare (z. B. BTC→WBTC), bei denen Wrapping <b>kein</b> Tausch ist (Begründung im AuditLog).</summary>
    public IReadOnlySet<string> WrappingNotATaxEvent { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Mindest-Konfidenz für die automatische Zuordnung eigener Transfers.</summary>
    public decimal TransferMinConfidence { get; init; } = 0.6m;

    /// <summary>Zeitfenster zwischen Auszahlung und Eingang (Standard 48 Stunden).</summary>
    public TimeSpan TransferWindow { get; init; } = TimeSpan.FromHours(48);

    /// <summary>Höchstabweichung der Mengen beim Transfer-Abgleich als Anteil (Netzwerkgebühr), Standard 2 %.</summary>
    public decimal TransferAmountTolerance { get; init; } = 0.02m;

    public bool IsTauschkrypto(string symbol) => !NotTauschkrypto.Contains(symbol);
}
