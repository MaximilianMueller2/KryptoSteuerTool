namespace KryptoSteuer.Domain.Ledger;

/// <summary>
/// Unveränderte Rohzeile oder Roh-JSON aus einem Import. Wird nie geändert oder gelöscht;
/// Korrekturen sind eigene Overrides (siehe <see cref="AuditLogEntry"/>).
/// </summary>
public sealed class RawRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Source { get; init; }
    public string? ExternalId { get; init; }
    /// <summary>Rohinhalt (Zeile oder JSON), exakt wie importiert.</summary>
    public required string Payload { get; init; }
    /// <summary>SHA-256 der Quelldatei bzw. der API-Antwort.</summary>
    public required string FileHash { get; init; }
    public required Guid ImportRunId { get; init; }
    public required DateTimeOffset ImportedAtUtc { get; init; }

    /// <summary>(Quelle, externe ID), Fallback: Hash des Rohinhalts.</summary>
    public string DedupKey => IdempotencyKey.For(Source, ExternalId, Payload);
}
