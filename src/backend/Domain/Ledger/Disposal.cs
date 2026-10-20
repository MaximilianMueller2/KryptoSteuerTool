namespace KryptoSteuer.Domain.Ledger;

/// <summary>Veräußerung je verbrauchtem Lot.</summary>
public sealed class Disposal
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid TaxRunId { get; init; }
    public required Guid LotId { get; init; }
    public required Guid TransactionId { get; init; }
    public required decimal Amount { get; init; }
    public required decimal ProceedsEur { get; init; }
    public required decimal CostEur { get; init; }
    public required decimal FeeShareEur { get; init; }
    public required decimal GainEur { get; init; }
    public required int HoldingDays { get; init; }
    public required bool IsTaxable { get; init; }
    public required string RuleSetVersion { get; init; }
}
