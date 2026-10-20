namespace KryptoSteuer.Domain.Ledger;

/// <summary>Ein Berechnungslauf mit Parametern, Regelsatz, Eingabe-Hash und Ergebnis-Snapshot.</summary>
public sealed class TaxRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required int TaxYear { get; init; }
    public required string RuleSetVersion { get; init; }
    /// <summary>Parameter als JSON (z. B. Verbrauchsfolge, Reform-Schalter).</summary>
    public required string ParametersJson { get; init; }
    /// <summary>SHA-256 der Eingabedaten; gleiche Eingabe = gleicher Hash = reproduzierbar.</summary>
    public required string InputHash { get; init; }
    public string? ResultSnapshotJson { get; set; }
}
