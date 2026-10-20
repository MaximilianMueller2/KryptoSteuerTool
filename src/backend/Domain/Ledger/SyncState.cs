namespace KryptoSteuer.Domain.Ledger;

/// <summary>Letzter erfolgreich synchronisierter Zeitpunkt je Konto und Endpunkt (inkrementeller Sync).</summary>
public sealed class SyncState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid AccountId { get; init; }
    /// <summary>Abschnittsart des Endpunkts, z. B. "spot-fills".</summary>
    public required string Endpoint { get; init; }
    public required DateTimeOffset LastSuccessfulUntilUtc { get; set; }
}
