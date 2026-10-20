namespace KryptoSteuer.Domain.Ledger;

/// <summary>Normalisierte Buchung mit Beinen. Rohdaten bleiben über <see cref="RawRecordIds"/> erreichbar.</summary>
public sealed class Transaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required TransactionType Type { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required Guid AccountId { get; init; }
    public required string Source { get; init; }
    public string? ExternalId { get; init; }
    /// <summary>Hash des Rohinhalts als Fallback, wenn es keine externe ID gibt.</summary>
    public string? ContentHash { get; init; }
    public TransactionStatus Status { get; set; } = TransactionStatus.Ok;
    public string? Note { get; set; }
    public List<TransactionLeg> Legs { get; init; } = [];
    public List<Guid> RawRecordIds { get; init; } = [];

    public string DedupKey => IdempotencyKey.For(Source, ExternalId, ContentHash ?? string.Empty);

    /// <summary>Prüft die Invarianten und liefert die Verstöße (leer = gültig).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (TimestampUtc.Offset != TimeSpan.Zero)
            errors.Add("Der Zeitstempel muss in UTC vorliegen.");

        foreach (var leg in Legs)
        {
            if (leg.Amount <= 0)
                errors.Add("Die Menge eines Beins muss größer als 0 sein.");
            if (leg.IsFee && leg.Direction != LegDirection.Out)
                errors.Add("Gebührenbeine müssen ausgehend sein.");
        }

        var fees = Legs.Count(l => l.IsFee);
        var main = Legs.Where(l => !l.IsFee).ToList();
        var ins = main.Count(l => l.Direction == LegDirection.In);
        var outs = main.Count(l => l.Direction == LegDirection.Out);

        if (fees > 1)
            errors.Add("Eine Buchung hat höchstens ein Gebührenbein.");

        switch (Type)
        {
            case TransactionType.Buy:
            case TransactionType.Sell:
            case TransactionType.Trade:
                if (ins != 1 || outs != 1)
                    errors.Add($"{Type} braucht genau ein In- und ein Out-Bein.");
                break;

            case TransactionType.Deposit:
            case TransactionType.TransferIn:
            case TransactionType.Staking:
            case TransactionType.Lending:
            case TransactionType.Airdrop:
            case TransactionType.Mining:
            case TransactionType.Bonus:
            case TransactionType.Interest:
            case TransactionType.Dividend:
            case TransactionType.GiftIn:
                if (ins != 1 || outs != 0)
                    errors.Add($"{Type} braucht genau ein In-Bein und kein Out-Bein.");
                break;

            case TransactionType.Withdrawal:
            case TransactionType.TransferOut:
            case TransactionType.Fee:
            case TransactionType.Spend:
            case TransactionType.GiftOut:
                if (outs != 1 || ins != 0)
                    errors.Add($"{Type} braucht genau ein Out-Bein und kein In-Bein.");
                break;

            case TransactionType.Unknown:
                // Unbekannte Typen werden unverändert zur Prüfung gespeichert; Beine sind frei.
                if (Status != TransactionStatus.NeedsReview)
                    errors.Add("Unbekannte Typen müssen den Status NeedsReview haben.");
                break;

            case TransactionType.FuturesPnL:
            case TransactionType.FundingFee:
            case TransactionType.Liquidation:
                if (ins + outs != 1)
                    errors.Add($"{Type} braucht genau ein Bein (Gewinn = In, Verlust = Out).");
                break;
        }

        return errors;
    }

    public void EnsureValid()
    {
        var errors = Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
    }
}
