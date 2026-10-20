namespace KryptoSteuer.Domain.Ledger;

/// <summary>EUR-Kurs eines Assets zu einem Zeitpunkt, mit Quelle (Nachvollziehbarkeit).</summary>
public sealed class Price
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid AssetId { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required decimal EurPrice { get; init; }
    public required string Source { get; init; }
}
