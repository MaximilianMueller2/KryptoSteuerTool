namespace KryptoSteuer.Domain.Ledger;

/// <summary>Anschaffungslos; wird später von der Steuer-Engine befüllt.</summary>
public sealed class Lot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid AssetId { get; init; }
    public required Guid AccountId { get; init; }
    public required decimal Amount { get; init; }
    public required decimal RemainingAmount { get; set; }
    public required DateTimeOffset AcquiredAtUtc { get; init; }
    public required decimal CostEur { get; init; }
    public required Guid SourceTransactionId { get; init; }
    public required TaxRegime Regime { get; init; }
}
