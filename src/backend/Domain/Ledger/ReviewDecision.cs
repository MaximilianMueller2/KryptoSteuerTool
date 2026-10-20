namespace KryptoSteuer.Domain.Ledger;

public enum ReviewDecisionKind
{
    /// <summary>Auszahlung ohne Gegenstück ging auf ein eigenes Konto (steuerneutral, Lots wandern mit).</summary>
    TransferToOwnAccount,
    /// <summary>Buchung zur Prüfung wurde angesehen und bleibt ohne Wirkung auf die Berechnung (z. B. Wertpapier-Kapitalmaßnahme).</summary>
    Acknowledged,
}

/// <summary>
/// Entscheidung des Nutzers zu einer Buchung. Die Rohdaten bleiben unverändert; die Entscheidung ist ein Override,
/// jede Änderung steht im Audit-Log. Je Buchung gibt es höchstens eine Entscheidung.
/// </summary>
public sealed class ReviewDecision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required ReviewDecisionKind Kind { get; init; }
    public required Guid TransactionId { get; init; }
    /// <summary>Zielkonto bei <see cref="ReviewDecisionKind.TransferToOwnAccount"/>.</summary>
    public Guid? TargetAccountId { get; init; }
    public string? Note { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
