using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Importers.Bitget;

/// <summary>Baut Buchungen aus einer Rohzeile; gemeinsam für API- und CSV-Importer von Bitget.</summary>
internal sealed class BitgetTxBuilder(RawRecord record, ImportContext context)
{
    public IReadOnlyList<Transaction> One(Transaction t) => [t];

    public TransactionLeg Leg(string symbol, decimal amount, LegDirection direction, bool isFee = false) =>
        new() { AssetId = context.Assets.Resolve(symbol), Amount = amount, Direction = direction, IsFee = isFee };

    public Transaction Build(
        TransactionType type, DateTimeOffset time, List<TransactionLeg> legs,
        TransactionStatus status = TransactionStatus.Ok, string? note = null) => new()
    {
        Type = type, TimestampUtc = time, AccountId = context.AccountId, Source = record.Source,
        ExternalId = record.ExternalId, ContentHash = record.ContentHash, Status = status, Note = note,
        Legs = legs, RawRecordIds = [record.Id],
    };

    /// <summary>Unbekannte Buchungsart: nie verwerfen, sondern NeedsReview mit Rohzeile.</summary>
    public Transaction Review(DateTimeOffset time, string typeName, List<TransactionLeg> legs)
    {
        context.AddUnknownType(typeName);
        return Build(TransactionType.Unknown, time, legs, TransactionStatus.NeedsReview, $"Unbekannter Typ '{typeName}'");
    }

    /// <summary>Bekannte, aber steuerlich irrelevante Buchung (z. B. interner Transfer): gespeichert mit Status Ignored.</summary>
    public Transaction Ignored(DateTimeOffset time, string typeName, string reason) =>
        Build(TransactionType.Unknown, time, [], TransactionStatus.Ignored, $"{typeName}: {reason}");
}

/// <summary>
/// Zuordnung der Bitget-Futures-Buchungsarten (API-<c>businessType</c> bzw. CSV-Spalte <c>Type</c>) zu Buchungen.
/// API und CSV nutzen dieselbe Zuordnung, damit dieselbe Buchung aus beiden Quellen denselben Fingerabdruck hat.
/// </summary>
internal static class BitgetFuturesMapper
{
    public static Transaction Map(
        BitgetTxBuilder b, string label, string coin, decimal amount, decimal fee, DateTimeOffset time, string? symbol)
    {
        var t = label.ToLowerInvariant();
        var feeAbs = Math.Abs(fee);
        var note = string.IsNullOrWhiteSpace(symbol) || symbol == "NULL" ? label : $"{label} {symbol}";

        TransactionLeg Signed() => b.Leg(coin, Math.Abs(amount), amount < 0 ? LegDirection.Out : LegDirection.In);

        List<TransactionLeg> WithFee(TransactionLeg main)
        {
            var legs = new List<TransactionLeg> { main };
            if (feeAbs > 0) legs.Add(b.Leg(coin, feeAbs, LegDirection.Out, isFee: true));
            return legs;
        }

        Transaction FeeOnly() => feeAbs > 0
            ? b.Build(TransactionType.Fee, time, [b.Leg(coin, feeAbs, LegDirection.Out)], note: note)
            : b.Ignored(time, label, "Betrag 0");

        // Zusätzlicher Verlust nach Liquidation (Bitget schreibt "captital").
        if (t.Contains("risk_capt") || t.Contains("risk_capital"))
            return amount == 0 ? FeeOnly() : b.Build(TransactionType.Liquidation, time, WithFee(Signed()), note: note);

        if (t.Contains("force") || t.Contains("burst") || t.Contains("liq"))
            return amount == 0 ? FeeOnly() : b.Build(TransactionType.Liquidation, time, WithFee(Signed()), note: note);

        if (t.Contains("settle_fee") || t.Contains("funding"))
            return amount == 0
                ? b.Ignored(time, label, "Betrag 0")
                : b.Build(TransactionType.FundingFee, time, [Signed()], note: note);

        if (t.StartsWith("open_", StringComparison.Ordinal) || t.StartsWith("close_", StringComparison.Ordinal)
            || t.Contains("_close_"))
            return amount == 0 ? FeeOnly() : b.Build(TransactionType.FuturesPnL, time, WithFee(Signed()), note: note);

        // Gutschriften von Bitget (Bonus, Gutschein, Grant).
        if (t.Contains("grant") || t.Contains("bonus") || t.Contains("cash_gift"))
            return amount > 0
                ? b.Build(TransactionType.Bonus, time, [Signed()], note: note)
                : b.Review(time, label, amount == 0 ? [] : [Signed()]);

        // Umbuchungen innerhalb des Kontos: Transfers zwischen Spot und Futures, Margin- und Hebelanpassungen.
        if (t.StartsWith("adjust", StringComparison.Ordinal) || t.Contains("transfer")
            || t.StartsWith("trans_", StringComparison.Ordinal) || t.Contains("margin"))
            return b.Ignored(time, label, "interne Buchung (Transfer/Margin) zwischen Bitget-Konten");

        return b.Review(time, label.Length > 0 ? label : "futures-bill", amount == 0 ? [] : [Signed()]);
    }
}
