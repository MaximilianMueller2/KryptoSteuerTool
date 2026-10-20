namespace KryptoSteuer.Domain.Ledger;

/// <summary>Jede manuelle Änderung (Override) mit Vorher/Nachher und Begründung.</summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string EntityType { get; init; }
    public required Guid EntityId { get; init; }
    public string? BeforeJson { get; init; }
    public string? AfterJson { get; init; }
    public required string Reason { get; init; }
}
