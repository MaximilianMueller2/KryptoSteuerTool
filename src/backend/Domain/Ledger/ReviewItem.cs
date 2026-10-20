namespace KryptoSteuer.Domain.Ledger;

/// <summary>Eintrag der Review-Queue: etwas, das der Steuerbericht nicht sicher berechnen kann (Ausbau in P11).</summary>
public sealed class ReviewItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required ReviewItemKind Kind { get; init; }
    public required string Reason { get; set; }
    public Guid? AccountId { get; init; }
    public Guid? AssetId { get; init; }
    /// <summary>Details als JSON (z. B. Börsen-Saldo, berechneter Bestand, Differenz).</summary>
    public string? DetailsJson { get; set; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ReviewItemStatus Status { get; set; } = ReviewItemStatus.Open;
}
