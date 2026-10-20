using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Model;
using KryptoSteuer.TaxEngine.Rules;

namespace KryptoSteuer.TaxEngine.Engine;

/// <summary>
/// Jahresaggregation: § 23 (Gewinne/Verluste innerhalb der Frist, Freigrenze, Verlustvortrag), § 22 Nr. 3 (Freigrenze 256 EUR),
/// Futures getrennt (Anlage KAP) und optional der Reform-Regelsatz. Beträge ungerundet; gerundet wird erst im Report.
/// </summary>
public static class YearlyAggregator
{
    public static YearSummary Aggregate(TaxEngineInput input, TaxEngineResult result, YearInputs inputs)
    {
        var year = inputs.Year;
        var reform = input.Settings.ApplyReform2027;

        // ---- § 23 ----
        var all = result.Disposals.Where(d => d.TaxYear == year).ToList();
        var within = all.Where(d => d.Treatment == TaxTreatment.Section23Taxable).ToList();
        var gains = within.Where(d => d.GainEur > 0).Sum(d => d.GainEur);
        var losses = within.Where(d => d.GainEur < 0).Sum(d => -d.GainEur);
        var net = gains - losses;
        var total = net + inputs.OtherPrivateSalesGainEur;
        var freigrenze = input.Law.Get("Freigrenze23", year);
        // Die Freigrenze gilt für den Gesamtgewinn des Jahres; ein Verlustvortrag wird erst abgezogen, wenn sie überschritten ist.
        var below = total < freigrenze;

        decimal used = 0m, taxable = 0m, carryOut;
        if (total <= 0)
        {
            carryOut = inputs.LossCarryForwardInEur - total;
        }
        else if (below)
        {
            carryOut = inputs.LossCarryForwardInEur;
        }
        else
        {
            used = Math.Min(inputs.LossCarryForwardInEur, total);
            taxable = total - used;
            carryOut = inputs.LossCarryForwardInEur - used;
        }

        var byAsset = within
            .GroupBy(d => (d.AccountId, d.AssetId))
            .OrderBy(g => g.Key.AccountId).ThenBy(g => g.Key.AssetId)
            .Select(g => new AssetResult(g.Key.AccountId, g.Key.AssetId,
                g.Where(d => d.GainEur > 0).Sum(d => d.GainEur), g.Where(d => d.GainEur < 0).Sum(d => -d.GainEur), g.Sum(d => d.GainEur),
                g.Sum(d => d.ProceedsEur), g.Sum(d => d.CostEur), g.Sum(d => d.FeeEur)))
            .ToList();

        var section23 = new Section23Summary(year,
            within.Sum(d => d.ProceedsEur), within.Sum(d => d.CostEur), within.Sum(d => d.FeeEur),
            gains, losses, net, inputs.OtherPrivateSalesGainEur, total, freigrenze, below,
            inputs.LossCarryForwardInEur, used, taxable, carryOut,
            all.Where(d => d.Treatment == TaxTreatment.TaxFreeAfterHoldingPeriod).Sum(d => d.GainEur), byAsset);

        // ---- § 22 Nr. 3 ----
        var incomes = result.Incomes.Where(i => i.TaxYear == year && i.CountsAsIncome && !(reform && i.Regime == TaxRegime.Neu)).ToList();
        var incomeSum = incomes.Sum(i => i.ValueEur);
        var net22 = incomeSum - inputs.Section22ExpensesEur;
        var freigrenze22 = input.Law.Get("Freigrenze22Nr3", year);
        var below22 = net22 < freigrenze22;
        var section22 = new Section22Summary(year, incomeSum, inputs.Section22ExpensesEur, net22, freigrenze22, below22,
            below22 || net22 <= 0 ? 0m : net22,
            incomes.GroupBy(i => i.Kind).ToDictionary(g => g.Key, g => g.Sum(i => i.ValueEur)));

        // ---- Futures ----
        var fut = result.Futures.Where(f => f.TaxYear == year).ToList();
        var pnl = fut.Where(f => f.Kind == FuturesKind.RealizedPnl).ToList();
        var futures = new FuturesSummary(year,
            pnl.Where(f => f.AmountEur > 0).Sum(f => f.AmountEur),
            pnl.Where(f => f.AmountEur < 0).Sum(f => f.AmountEur),
            fut.Where(f => f.Kind == FuturesKind.FundingFee).Sum(f => f.AmountEur),
            // Gebühren als positive Kosten: Gebührenbeine plus eigenständige Gebührenbuchungen (deren Betrag ist negativ).
            fut.Sum(f => f.FeeEur) - fut.Where(f => f.Kind == FuturesKind.TradingFee).Sum(f => f.AmountEur),
            fut.Where(f => f.Kind == FuturesKind.Liquidation).Sum(f => f.AmountEur),
            fut.Sum(f => f.AmountEur - f.FeeEur),
            fut.Count);

        // ---- Reform ----
        ReformSummary? reformSummary = null;
        if (reform && year >= 2027)
        {
            var capital = all.Where(d => d.Treatment == TaxTreatment.ReformCapital).Sum(d => d.GainEur)
                + result.Incomes.Where(i => i.TaxYear == year && i.CountsAsIncome && i.Regime == TaxRegime.Neu).Sum(i => i.ValueEur);
            var pauschbetrag = input.Reform.Get(inputs.JointAssessment ? "SparerPauschbetragJoint" : "SparerPauschbetragSingle", year);
            var taxableCapital = Math.Max(0m, capital - pauschbetrag);
            var flat = taxableCapital * input.Reform.Get("FlatTaxRate", year);
            var soli = flat * input.Reform.Get("SolidarityRate", year);
            reformSummary = new ReformSummary(year, capital, pauschbetrag, taxableCapital, flat, soli, flat + soli);
        }

        var findings = result.Findings.Where(f => f.TaxYear is null || f.TaxYear == year).ToList();
        return new YearSummary(year, input.Law.Id, input.Law.Version, reform ? input.Reform.Id : null,
            section23, section22, futures, reformSummary, findings);
    }

    /// <summary>Netzwerkgebühren bei eigenen Transfers im Jahr, gesondert ausgewiesen ("mit Berater klären").</summary>
    public static NetworkFeeSummary NetworkFees(TaxEngineResult result, int year) =>
        new(year, result.NetworkFees.Where(n => n.TaxYear == year).GroupBy(n => n.AssetId).ToDictionary(g => g.Key, g => g.Sum(n => n.Quantity)),
            result.NetworkFees.Count(n => n.TaxYear == year));
}
