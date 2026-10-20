using FsCheck;
using FsCheck.Xunit;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Engine;
using KryptoSteuer.TaxEngine.Model;
using KryptoSteuer.TaxEngine.Rules;

namespace KryptoSteuer.TaxEngine.Tests;

/// <summary>
/// Property-based Tests (FsCheck): Für zufällige, in sich gültige Buchungsfolgen (Käufe, Verkäufe, Transfers zwischen zwei Konten)
/// gilt zu jedem Zeitpunkt: Summe der Lot-Restmengen = Bestand je Konto und Asset; Kosten bleiben erhalten; nichts wird gestoppt.
/// </summary>
public class LotInvariantProperties
{
    private static readonly Asset Btc = new() { Id = GoldenScenario.AssetId("BTC"), Symbol = "BTC", Class = AssetClass.Crypto };
    private static readonly Asset Eth = new() { Id = GoldenScenario.AssetId("ETH"), Symbol = "ETH", Class = AssetClass.Crypto };
    private static readonly Asset Eur = new() { Id = GoldenScenario.AssetId("EUR"), Symbol = "EUR", Class = AssetClass.Fiat };
    private static readonly Guid[] Accounts = [GoldenScenario.AccountId("A"), GoldenScenario.AccountId("B")];
    private static readonly Asset[] Coins = [Btc, Eth];

    // Anteile bei Teilverbräuchen werden auf 28 Stellen verteilt; die Prüfungen vergleichen mit einer Toleranz weit unter einem Cent.
    private const decimal Tolerance = 0.000000001m;

    private static bool Same(decimal a, decimal b) => Math.Abs(a - b) <= Tolerance;

    private sealed record Built(List<Transaction> Transactions, Dictionary<(Guid, Guid), decimal> Holdings, decimal TotalCost, decimal TotalSoldProceeds, decimal SoldQuantity);

    /// <summary>Baut aus einem Startwert eine gültige Folge: Verkäufe nie über den Bestand, Transfers mit kleiner Gebühr.</summary>
    private static Built Build(int seed, int length, bool transferFees)
    {
        var rng = new System.Random(seed);
        var holdings = new Dictionary<(Guid, Guid), decimal>();
        var txs = new List<Transaction>();
        var t0 = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        decimal totalCost = 0, proceeds = 0, sold = 0;
        var n = 0;

        Transaction Make(TransactionType type, Guid acc, DateTimeOffset at, params TransactionLeg[] legs) => new()
        {
            Id = Guid.NewGuid(), Type = type, TimestampUtc = at, AccountId = acc, Source = "prop", ExternalId = $"p{++n:D4}", Legs = [.. legs],
        };

        TransactionLeg Leg(Asset a, LegDirection d, decimal amt, bool fee = false) => new() { AssetId = a.Id, Direction = d, Amount = amt, IsFee = fee };

        for (var i = 0; i < length; i++)
        {
            var at = t0.AddHours(i * 5);
            var acc = Accounts[rng.Next(2)];
            var coin = Coins[rng.Next(2)];
            var held = holdings.GetValueOrDefault((acc, coin.Id));
            var kind = rng.Next(4);

            if (kind == 0 || held < 0.001m)
            {
                var qty = Math.Round((decimal)rng.Next(1, 500) / 100, 2);
                var cost = Math.Round((decimal)rng.Next(100, 90000) / 10, 1);
                txs.Add(Make(TransactionType.Buy, acc, at, Leg(coin, LegDirection.In, qty), Leg(Eur, LegDirection.Out, cost)));
                holdings[(acc, coin.Id)] = held + qty;
                totalCost += cost;
            }
            else if (kind == 1 || kind == 2)
            {
                var qty = Math.Round(held * rng.Next(1, 101) / 100, 2);
                if (qty <= 0) continue;
                var eur = Math.Round((decimal)rng.Next(100, 90000) / 10, 1);
                txs.Add(Make(TransactionType.Sell, acc, at, Leg(coin, LegDirection.Out, qty), Leg(Eur, LegDirection.In, eur)));
                holdings[(acc, coin.Id)] = held - qty;
                proceeds += eur;
                sold += qty;
            }
            else
            {
                var to = Accounts[0] == acc ? Accounts[1] : Accounts[0];
                var qty = Math.Round(held * rng.Next(10, 101) / 100, 2);
                var fee = transferFees ? Math.Round(qty * 0.005m, 2) : 0m;
                if (qty - fee <= 0.01m) continue;
                var outLegs = new List<TransactionLeg> { Leg(coin, LegDirection.Out, qty - fee) };
                if (fee > 0) outLegs.Add(Leg(coin, LegDirection.Out, fee, fee: true));
                txs.Add(Make(TransactionType.Withdrawal, acc, at, [.. outLegs]));
                txs.Add(Make(TransactionType.Deposit, to, at.AddHours(1), Leg(coin, LegDirection.In, qty - fee)));
                holdings[(acc, coin.Id)] = held - qty;
                holdings[(to, coin.Id)] = holdings.GetValueOrDefault((to, coin.Id)) + qty - fee;
            }
        }

        return new Built(txs, holdings, totalCost, proceeds, sold);
    }

    private static TaxEngineResult Run(IReadOnlyList<Transaction> txs, TaxSettings? settings = null) =>
        TaxCalculator.Calculate(TaxEngineInput.Create(txs, [Btc, Eth, Eur], settings: settings));

    [Property(MaxTest = 150)]
    public bool Summe_der_Lot_Restmengen_ist_der_Bestand_je_Konto_zu_jedem_Zeitpunkt(int seed)
    {
        var built = Build(seed, 24, transferFees: true);
        for (var prefix = 1; prefix <= built.Transactions.Count; prefix += 3)
        {
            var part = built.Transactions.Take(prefix).ToList();
            // Eine Auszahlung gehört zusammen mit ihrem Eingang zur Folge (sonst gäbe es zu Recht einen Befund).
            if (part.Count > 0 && part[^1].Type == TransactionType.Withdrawal) part.RemoveAt(part.Count - 1);
            var expected = new Dictionary<(Guid, Guid), decimal>();
            // Gegenprobe unabhängig von der Engine: Bestand aus den Buchungen der Teilfolge.
            foreach (var t in part)
                foreach (var l in t.Legs.Where(l => l.AssetId != Eur.Id))
                {
                    var key = (t.AccountId, l.AssetId);
                    expected[key] = expected.GetValueOrDefault(key) + (l.Direction == LegDirection.In ? l.Amount : -l.Amount);
                }

            var result = Run(part);
            var lotSums = result.Lots.GroupBy(l => (l.AccountId, l.AssetId)).ToDictionary(g => g.Key, g => g.Sum(l => l.RemainingQuantity));

            if (result.Findings.Count != 0) return false;
            foreach (var (key, qty) in expected.Where(kv => kv.Value != 0))
                if (!Same(lotSums.GetValueOrDefault(key), qty)) return false;
            // Auszahlungen bei Transfers verlassen das Quellkonto komplett (Menge plus Gebühr); der Bestand ist daher genau erwartet.
            if (lotSums.Any(kv => kv.Value < 0)) return false;
        }

        return true;
    }

    [Property(MaxTest = 150)]
    public bool Kosten_bleiben_erhalten_Disposal_Kosten_plus_Restkosten_gleich_Anschaffungskosten(int seed)
    {
        var built = Build(seed, 24, transferFees: false);
        var result = Run(built.Transactions);

        var disposalCost = result.Disposals.Sum(d => d.CostEur);
        var remainingCost = result.Lots.Sum(l => l.RemainingCostEur);
        return result.Findings.Count == 0 && Same(disposalCost + remainingCost, built.TotalCost);
    }

    [Property(MaxTest = 150)]
    public bool Verkaufte_Mengen_und_Erloese_werden_vollstaendig_auf_Disposals_verteilt(int seed)
    {
        var built = Build(seed, 24, transferFees: true);
        var result = Run(built.Transactions);

        return result.Findings.Count == 0
            && Same(result.Disposals.Sum(d => d.Quantity), built.SoldQuantity)
            && Same(result.Disposals.Sum(d => d.ProceedsEur), built.TotalSoldProceeds)
            && result.Disposals.All(d => d.GainEur == d.ProceedsEur - d.CostEur - d.FeeEur)
            && result.Disposals.All(d => d.Quantity > 0);
    }

    [Property(MaxTest = 100)]
    public bool Durchschnittsmethode_erhaelt_ebenfalls_Mengen_und_Kosten(int seed)
    {
        var built = Build(seed, 24, transferFees: false);
        var result = Run(built.Transactions, new TaxSettings { Method = ConsumptionMethod.AverageCost });

        var lotSums = result.Lots.GroupBy(l => (l.AccountId, l.AssetId)).ToDictionary(g => g.Key, g => g.Sum(l => l.RemainingQuantity));
        var quantitiesOk = built.Holdings.Where(kv => kv.Value != 0).All(kv => Same(lotSums.GetValueOrDefault(kv.Key), kv.Value));
        return result.Findings.Count == 0 && quantitiesOk
            && Same(result.Disposals.Sum(d => d.CostEur) + result.Lots.Sum(l => l.RemainingCostEur), built.TotalCost);
    }

    [Property(MaxTest = 100)]
    public bool Ergebnis_hangt_nicht_von_der_Reihenfolge_der_Eingabe_ab(int seed)
    {
        var txs = Build(seed, 24, true).Transactions;
        var a = System.Text.Json.JsonSerializer.Serialize(Run(txs).Disposals);
        var b = System.Text.Json.JsonSerializer.Serialize(Run([.. txs.AsEnumerable().Reverse()]).Disposals);
        return a == b;
    }
}
