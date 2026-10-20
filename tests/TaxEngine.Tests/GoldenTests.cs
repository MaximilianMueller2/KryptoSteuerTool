using System.Globalization;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Engine;
using KryptoSteuer.TaxEngine.Model;

namespace KryptoSteuer.TaxEngine.Tests;

/// <summary>
/// Golden Tests (Pflicht in CI): Eingabebuchungen und von Hand gerechnetes Ergebnis je Szenario (Golden/*.json, Rechnung in Golden/*.md).
/// Eine Änderung des erwarteten Ergebnisses braucht einen ADR (docs/adr).
/// </summary>
public class GoldenTests
{
    public static IEnumerable<object[]> Scenarios => GoldenScenario.All();

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public void Mindestens_15_Szenarien_mit_Hand_Rechnung_vorhanden()
    {
        var names = GoldenScenario.All().Select(o => (string)o[0]).ToList();

        names.Count.Should().BeGreaterThanOrEqualTo(15);
        names.Should().OnlyContain(n => File.Exists(Path.Combine(AppContext.BaseDirectory, "Golden", n + ".md")), "jedes Szenario hat seine Rechnung als Markdown");
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Szenario_ergibt_das_von_Hand_gerechnete_Ergebnis(string name)
    {
        var s = GoldenScenario.Load(name);
        var input = s.BuildInput();
        input.Transactions.Should().OnlyContain(t => t.Validate().Count == 0, "die Eingabebuchungen müssen selbst gültig sein");

        var result = TaxCalculator.Calculate(input);
        var year = YearlyAggregator.Aggregate(input, result, s.BuildYearInputs());
        var e = s.Expected;
        var txByExternal = input.Transactions.ToDictionary(t => t.ExternalId!, t => t);

        if (e.Section23 is { } x)
        {
            var a = year.Section23;
            if (x.Gains is not null) a.GainsEur.Should().Be(D(x.Gains), "Gewinne § 23");
            if (x.Losses is not null) a.LossesEur.Should().Be(D(x.Losses), "Verluste § 23");
            if (x.Net is not null) a.NetEur.Should().Be(D(x.Net), "Netto § 23");
            if (x.Freigrenze is not null) a.FreigrenzeEur.Should().Be(D(x.Freigrenze));
            if (x.BelowFreigrenze is not null) a.BelowFreigrenze.Should().Be(x.BelowFreigrenze.Value, "Freigrenze");
            if (x.Taxable is not null) a.TaxableEur.Should().Be(D(x.Taxable), "steuerpflichtig § 23");
            if (x.LossCarryOut is not null) a.LossCarryOutEur.Should().Be(D(x.LossCarryOut), "Verlustvortrag neu");
            if (x.LossUsed is not null) a.LossUsedEur.Should().Be(D(x.LossUsed), "verrechneter Verlustvortrag");
            if (x.TaxFreeAfterPeriodNet is not null) a.TaxFreeAfterPeriodNetEur.Should().Be(D(x.TaxFreeAfterPeriodNet), "steuerfrei nach Frist");
        }

        if (e.Section22 is { } y)
        {
            var a = year.Section22;
            if (y.Income is not null) a.IncomeEur.Should().Be(D(y.Income));
            if (y.Net is not null) a.NetEur.Should().Be(D(y.Net));
            if (y.BelowFreigrenze is not null) a.BelowFreigrenze.Should().Be(y.BelowFreigrenze.Value);
            if (y.Taxable is not null) a.TaxableEur.Should().Be(D(y.Taxable));
        }

        if (e.Reform is { } r)
        {
            year.Reform.Should().NotBeNull("der Reform-Regelsatz ist eingeschaltet");
            var a = year.Reform!;
            if (r.Net is not null) a.NetCapitalGainEur.Should().Be(D(r.Net));
            if (r.Pauschbetrag is not null) a.PauschbetragEur.Should().Be(D(r.Pauschbetrag));
            if (r.Taxable is not null) a.TaxableEur.Should().Be(D(r.Taxable));
            if (r.FlatTax is not null) a.FlatTaxEur.Should().Be(D(r.FlatTax));
            if (r.Solidarity is not null) a.SolidarityEur.Should().Be(D(r.Solidarity));
            if (r.Total is not null) a.TotalTaxEur.Should().Be(D(r.Total));
        }

        if (e.Futures is { } f)
        {
            var a = year.Futures;
            if (f.RealizedGains is not null) a.RealizedGainsEur.Should().Be(D(f.RealizedGains));
            if (f.RealizedLosses is not null) a.RealizedLossesEur.Should().Be(D(f.RealizedLosses));
            if (f.Funding is not null) a.FundingNetEur.Should().Be(D(f.Funding));
            if (f.TradingFees is not null) a.TradingFeesEur.Should().Be(D(f.TradingFees));
            if (f.Liquidation is not null) a.LiquidationEur.Should().Be(D(f.Liquidation));
            if (f.Net is not null) a.NetEur.Should().Be(D(f.Net));
            if (f.Records is not null) a.Records.Should().Be(f.Records.Value);
        }

        if (e.Disposals is { } ds)
        {
            result.Disposals.Should().HaveCount(ds.Count, "Anzahl der Disposal-Datensätze");
            for (var i = 0; i < ds.Count; i++)
            {
                var exp = ds[i];
                var act = result.Disposals[i];
                var because = $"Disposal {i + 1} ({exp.Tx})";
                act.TransactionExternalId.Should().Be(exp.Tx, because);
                if (exp.LotTx is not null)
                    result.Lots.Single(l => l.Id == act.LotId).SourceExternalId.Should().Be(exp.LotTx, because + ": verbrauchtes Lot");
                if (exp.Qty is not null) act.Quantity.Should().Be(D(exp.Qty), because);
                if (exp.Proceeds is not null) act.ProceedsEur.Should().Be(D(exp.Proceeds), because);
                if (exp.Cost is not null) act.CostEur.Should().Be(D(exp.Cost), because);
                if (exp.Fee is not null) act.FeeEur.Should().Be(D(exp.Fee), because);
                if (exp.Gain is not null) act.GainEur.Should().Be(D(exp.Gain), because);
                if (exp.HoldingDays is not null) act.HoldingDays.Should().Be(exp.HoldingDays.Value, because);
                if (exp.Within is not null) act.WithinHoldingPeriod.Should().Be(exp.Within.Value, because);
                if (exp.TaxFreeFrom is not null) act.TaxFreeFrom.Should().Be(DateOnly.Parse(exp.TaxFreeFrom, CultureInfo.InvariantCulture), because);
                if (exp.Regime is not null) act.Regime.Should().Be(Enum.Parse<TaxRegime>(exp.Regime), because);
                if (exp.Treatment is not null) act.Treatment.Should().Be(Enum.Parse<TaxTreatment>(exp.Treatment), because);
            }
        }

        if (e.Lots is { } ls)
        {
            // Restlots mit Restmenge > 0, in Anlegereihenfolge
            var remaining = result.Lots.Where(l => l.RemainingQuantity > 0).ToList();
            remaining.Should().HaveCount(ls.Count, "Anzahl der Restlots");
            for (var i = 0; i < ls.Count; i++)
            {
                var exp = ls[i];
                var act = remaining[i];
                act.AccountId.Should().Be(GoldenScenario.AccountId(exp.Account));
                act.AssetId.Should().Be(GoldenScenario.AssetId(exp.Asset));
                if (exp.Remaining is not null) act.RemainingQuantity.Should().Be(D(exp.Remaining));
                if (exp.RemainingCost is not null) act.RemainingCostEur.Should().Be(D(exp.RemainingCost));
                if (exp.AcquiredAt is not null) act.AcquiredAtUtc.Should().Be(DateTimeOffset.Parse(exp.AcquiredAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
            }
        }

        if (e.Holdings is { } hs)
        {
            foreach (var (key, value) in hs)
            {
                var parts = key.Split('/');
                var held = result.Holdings.GetValueOrDefault((GoldenScenario.AccountId(parts[0]), GoldenScenario.AssetId(parts[1])));
                held.Should().Be(D(value), $"Bestand {key}");
            }

            if (hs.Count == 0) result.Holdings.Values.Should().OnlyContain(v => v == 0);
        }

        if (e.Findings is { } fs)
            result.Findings.Select(x => x.Kind.ToString()).Should().BeEquivalentTo(fs, "Befunde der Review-Queue (weder mehr noch weniger)");

        if (e.Transfers is { } tr) result.Transfers.Should().HaveCount(tr);
        if (e.NetworkFeeQuantity is not null) result.NetworkFees.Sum(n => n.Quantity).Should().Be(D(e.NetworkFeeQuantity));

        _ = txByExternal;
    }

    [Fact]
    public void Das_Ergebnis_ist_deterministisch_byte_gleich_bei_gleicher_Eingabe_auch_bei_anderer_Reihenfolge()
    {
        foreach (var name in GoldenScenario.All().Select(o => (string)o[0]))
        {
            var s = GoldenScenario.Load(name);
            var input = s.BuildInput();
            var shuffled = input with { Transactions = [.. input.Transactions.OrderByDescending(t => t.ExternalId)] };

            var a = System.Text.Json.JsonSerializer.Serialize(TaxCalculator.Calculate(input).Disposals);
            var b = System.Text.Json.JsonSerializer.Serialize(TaxCalculator.Calculate(shuffled).Disposals);
            var c = System.Text.Json.JsonSerializer.Serialize(TaxCalculator.Calculate(input).Disposals);

            b.Should().Be(a, $"{name}: die Reihenfolge der Eingabe darf das Ergebnis nicht ändern");
            c.Should().Be(a, $"{name}: wiederholte Berechnung");
        }
    }
}
