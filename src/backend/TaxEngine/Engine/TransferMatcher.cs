using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Rules;

namespace KryptoSteuer.TaxEngine.Engine;

/// <summary>Zuordnung einer Auszahlung zu einem Eingang zwischen zwei eigenen Konten.</summary>
public sealed record TransferMatch(Transaction Out, Transaction In, Guid AssetId, decimal OutQuantity, decimal InQuantity, decimal Confidence)
{
    public decimal NetworkFeeQuantity => OutQuantity - InQuantity;
}

public sealed record TransferMatchResult(
    IReadOnlyList<TransferMatch> Matches,
    IReadOnlyList<(Transaction Tx, string Suggestion)> UnmatchedOut,
    IReadOnlyList<Transaction> UnmatchedIn);

/// <summary>
/// Paart TransferOut/Withdrawal mit TransferIn/Deposit eigener Konten über Asset, Menge minus Netzwerkgebühr (Toleranz) und
/// Zeitfenster (Standard 48 h). Unter der Mindest-Konfidenz wird nicht gepaart, sondern ein Vorschlag für die Review-Queue geliefert.
/// </summary>
public static class TransferMatcher
{
    public static TransferMatchResult Match(
        IReadOnlyList<Transaction> candidates, IReadOnlyDictionary<Guid, Asset> assets, TaxSettings settings)
    {
        static bool IsOut(Transaction t) => t.Type is TransactionType.TransferOut or TransactionType.Withdrawal;
        static bool IsIn(Transaction t) => t.Type is TransactionType.TransferIn or TransactionType.Deposit;

        var outs = candidates.Where(IsOut).Where(t => Tracked(t, assets)).OrderBy(t => t.TimestampUtc).ThenBy(t => t.ExternalId, StringComparer.Ordinal).ToList();
        var ins = candidates.Where(IsIn).Where(t => Tracked(t, assets)).OrderBy(t => t.TimestampUtc).ThenBy(t => t.ExternalId, StringComparer.Ordinal).ToList();
        var freeIns = new List<Transaction>(ins);
        var matches = new List<TransferMatch>();
        var unmatchedOut = new List<(Transaction, string)>();

        foreach (var o in outs)
        {
            var (assetId, outQty) = Quantity(o);
            (Transaction Tx, decimal Qty, decimal Confidence)? best = null;
            (Transaction Tx, decimal Qty, decimal Confidence)? bestAny = null;

            foreach (var i in freeIns)
            {
                var (inAsset, inQty) = Quantity(i);
                if (inAsset != assetId || i.AccountId == o.AccountId) continue;
                var dt = i.TimestampUtc - o.TimestampUtc;
                if (dt < TimeSpan.Zero || dt > settings.TransferWindow) continue;
                if (inQty > outQty) continue;

                var diff = (outQty - inQty) / outQty;
                var confidence = Confidence(diff, dt, settings);
                if (bestAny is null || confidence > bestAny.Value.Confidence) bestAny = (i, inQty, confidence);
                if (diff <= settings.TransferAmountTolerance && confidence >= settings.TransferMinConfidence
                    && (best is null || confidence > best.Value.Confidence))
                    best = (i, inQty, confidence);
            }

            if (best is { } b)
            {
                freeIns.Remove(b.Tx);
                matches.Add(new TransferMatch(o, b.Tx, assetId, outQty, b.Qty, b.Confidence));
            }
            else
            {
                var hint = bestAny is { } a
                    ? $"Mögliches Gegenstück: Eingang {a.Tx.TimestampUtc:u} über {a.Qty} (Konfidenz {a.Confidence:0.00}, unter der Schwelle)."
                    : "Kein Eingang mit passender Menge im Zeitfenster gefunden.";
                unmatchedOut.Add((o, hint));
            }
        }

        return new TransferMatchResult(matches, unmatchedOut, freeIns);
    }

    /// <summary>Konfidenz 0..1: Mengengleichheit zählt am meisten, ein kurzer Abstand der Zeiten etwas.</summary>
    private static decimal Confidence(decimal relativeAmountDiff, TimeSpan dt, TaxSettings settings)
    {
        var tolerance = settings.TransferAmountTolerance <= 0 ? 0.0001m : settings.TransferAmountTolerance;
        var amountScore = Math.Max(0m, 1m - relativeAmountDiff / tolerance * 0.3m);
        var timeScore = 1m - (decimal)(dt.TotalSeconds / settings.TransferWindow.TotalSeconds) * 0.2m;
        return Math.Round(amountScore * 0.8m + timeScore * 0.2m, 4);
    }

    private static bool Tracked(Transaction t, IReadOnlyDictionary<Guid, Asset> assets) =>
        t.Legs.Any(l => !l.IsFee && assets.TryGetValue(l.AssetId, out var a) && a.Class is AssetClass.Crypto or AssetClass.Stablecoin);

    /// <summary>Menge, die das Konto verlässt (inklusive Gebührenbein im selben Coin) bzw. ankommt.</summary>
    internal static (Guid AssetId, decimal Quantity) Quantity(Transaction t)
    {
        var main = t.Legs.First(l => !l.IsFee);
        var fee = t.Legs.Where(l => l.IsFee && l.AssetId == main.AssetId && l.Direction == LegDirection.Out).Sum(l => l.Amount);
        return (main.AssetId, main.Direction == LegDirection.Out ? main.Amount + fee : main.Amount);
    }
}
