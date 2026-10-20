using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.TaxEngine.Engine;

/// <summary>Veränderliches Lot während der Berechnung. Restkosten gehen proportional mit der Restmenge zurück.</summary>
internal sealed class LotState
{
    public required Guid Id { get; init; }
    public required int Sequence { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid AssetId { get; init; }
    public required DateTimeOffset AcquiredAtUtc { get; init; }
    public required DateOnly AcquiredBerlinDate { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal CostEur { get; init; }
    public required TaxRegime Regime { get; init; }
    public required Model.LotKind Kind { get; init; }
    public required Guid SourceTransactionId { get; init; }
    public string? SourceExternalId { get; init; }

    public decimal Remaining { get; set; }
    public decimal RemainingCost { get; set; }
}

internal sealed record LotSlice(LotState Lot, decimal Quantity, decimal CostEur);

/// <summary>Verbrauchsfolge hinter einer Schnittstelle: FIFO (Standard) oder gleitender Durchschnitt.</summary>
internal interface IConsumptionStrategy
{
    /// <summary>Verbraucht <paramref name="quantity"/> aus den Lots (bereits in Verbrauchsreihenfolge) und liefert die Anteile.</summary>
    IReadOnlyList<LotSlice> Consume(IReadOnlyList<LotState> orderedLots, decimal quantity);
}

internal sealed class FifoStrategy : IConsumptionStrategy
{
    public IReadOnlyList<LotSlice> Consume(IReadOnlyList<LotState> orderedLots, decimal quantity)
    {
        var slices = new List<LotSlice>();
        var left = quantity;
        foreach (var lot in orderedLots)
        {
            if (left <= 0) break;
            if (lot.Remaining <= 0) continue;

            var take = Math.Min(left, lot.Remaining);
            // Restkosten proportional; bei vollständigem Verbrauch exakt die Restkosten (keine Rundungsreste).
            var cost = take == lot.Remaining ? lot.RemainingCost : lot.RemainingCost * take / lot.Remaining;
            lot.Remaining -= take;
            lot.RemainingCost -= cost;
            slices.Add(new LotSlice(lot, take, cost));
            left -= take;
        }

        return slices;
    }
}

/// <summary>
/// Gleitender Durchschnitt: Die Menge wird anteilig aus allen Lots entnommen (Anschaffungsdaten bleiben je Lot erhalten),
/// die Kosten je Einheit sind der Durchschnitt aller Restkosten. Muss für ein Jahr stetig angewendet werden.
/// </summary>
internal sealed class AverageCostStrategy : IConsumptionStrategy
{
    public IReadOnlyList<LotSlice> Consume(IReadOnlyList<LotState> orderedLots, decimal quantity)
    {
        var lots = orderedLots.Where(l => l.Remaining > 0).ToList();
        var totalQty = lots.Sum(l => l.Remaining);
        var totalCost = lots.Sum(l => l.RemainingCost);
        var slices = new List<LotSlice>();
        var qtyLeft = quantity;
        var costLeft = quantity == totalQty ? totalCost : totalCost * quantity / totalQty;

        for (var i = 0; i < lots.Count; i++)
        {
            var lot = lots[i];
            var last = i == lots.Count - 1;
            var take = last ? qtyLeft : quantity * lot.Remaining / totalQty;
            var cost = last ? costLeft : costLeft * take / qtyLeft;
            take = Math.Min(take, lot.Remaining);
            cost = Math.Min(cost, lot.RemainingCost);
            lot.Remaining -= take;
            lot.RemainingCost -= cost;
            slices.Add(new LotSlice(lot, take, cost));
            qtyLeft -= take;
            costLeft -= cost;
        }

        return slices;
    }
}
