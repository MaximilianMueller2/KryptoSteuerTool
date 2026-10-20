namespace KryptoSteuer.Domain.Ledger;

/// <summary>Verbindet TransferOut und TransferIn zweier eigener Konten (steuerneutral).</summary>
public sealed class TransferLink
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid TransferOutTransactionId { get; init; }
    public required Guid TransferInTransactionId { get; init; }
}
