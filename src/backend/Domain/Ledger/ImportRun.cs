namespace KryptoSteuer.Domain.Ledger;

/// <summary>
/// Ein Importlauf. Die Quelldatei wird roh gespeichert (SHA-256); bis zur Bestätigung ist der Lauf
/// <see cref="ImportRunStatus.Pending"/> und kann gelöscht werden. Danach ist er unveränderlich.
/// </summary>
public sealed class ImportRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Source { get; init; }
    public required string FileName { get; init; }
    public required string FileHash { get; init; }
    public required byte[] Content { get; init; }
    public required Guid AccountId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ImportRunStatus Status { get; set; } = ImportRunStatus.Pending;
    public DateTimeOffset? CommittedAtUtc { get; set; }
    /// <summary>Importbericht als JSON (Vorschau bzw. Ergebnis der Bestätigung).</summary>
    public required string ReportJson { get; set; }
}
