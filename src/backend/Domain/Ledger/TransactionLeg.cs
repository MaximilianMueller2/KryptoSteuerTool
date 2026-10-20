namespace KryptoSteuer.Domain.Ledger;

public sealed class TransactionLeg
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public required Guid AssetId { get; init; }
    /// <summary>Immer positiv; die Richtung steht in <see cref="Direction"/>.</summary>
    public required decimal Amount { get; init; }
    public required LegDirection Direction { get; init; }
    public bool IsFee { get; init; }
}
