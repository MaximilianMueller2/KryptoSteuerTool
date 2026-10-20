using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Model;

namespace KryptoSteuer.Application.Tax;

/// <summary>Aktueller Bestand je Konto und Asset aus den offenen Lots (Kosten in EUR, auf den Cent gerundet).</summary>
public sealed record HoldingView(Guid AccountId, string Account, string Asset, decimal Quantity, decimal CostEur, int Lots, DateOnly OldestAcquired, DateOnly NextTaxFreeFrom);

public static class HoldingsReport
{
    public static IReadOnlyList<HoldingView> Build(TaxEngineResult result, IReadOnlyDictionary<Guid, Asset> assets, IReadOnlyDictionary<Guid, string> accountNames) =>
        result.Lots.Where(l => l.RemainingQuantity > 0)
            .GroupBy(l => (l.AccountId, l.AssetId))
            .Select(g => new HoldingView(
                g.Key.AccountId, accountNames.GetValueOrDefault(g.Key.AccountId, g.Key.AccountId.ToString("N")[..8]),
                assets.TryGetValue(g.Key.AssetId, out var a) ? a.Symbol : g.Key.AssetId.ToString("N")[..8],
                g.Sum(l => l.RemainingQuantity), Math.Round(g.Sum(l => l.RemainingCostEur), 2, MidpointRounding.AwayFromZero),
                g.Count(), g.Min(l => l.AcquiredBerlinDate), g.Min(l => l.TaxFreeFrom)))
            .OrderBy(h => h.Account, StringComparer.Ordinal).ThenBy(h => h.Asset, StringComparer.Ordinal)
            .ToList();
}
