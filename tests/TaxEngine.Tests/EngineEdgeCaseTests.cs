using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Engine;
using KryptoSteuer.TaxEngine.Model;
using KryptoSteuer.TaxEngine.Pricing;
using KryptoSteuer.TaxEngine.Rules;

namespace KryptoSteuer.TaxEngine.Tests;

/// <summary>Randfälle der Engine: Befunde statt stiller Annahmen, Optionen, Einkunftsarten und Transfer-Sonderfälle.</summary>
public class EngineEdgeCaseTests
{
    private static readonly Asset Btc = Asset("BTC", AssetClass.Crypto);
    private static readonly Asset Eth = Asset("ETH", AssetClass.Crypto);
    private static readonly Asset Eur = Asset("EUR", AssetClass.Fiat);
    private static readonly Asset Usdt = Asset("USDT", AssetClass.Stablecoin);
    private static readonly Asset Stock = Asset("DE0000000001", AssetClass.Stock);
    private static readonly Asset Sol = Asset("SOL", AssetClass.Crypto);
    private static readonly Guid A = GoldenScenario.AccountId("A");
    private static readonly Guid B = GoldenScenario.AccountId("B");
    private static int _n;

    private static Asset Asset(string symbol, AssetClass cls) => new() { Id = GoldenScenario.AssetId(symbol), Symbol = symbol, Class = cls };
    private static DateTimeOffset T(string s) => DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);

    private static TransactionLeg In(Asset a, decimal amt, bool fee = false) => new() { AssetId = a.Id, Direction = LegDirection.In, Amount = amt, IsFee = fee };
    private static TransactionLeg Out(Asset a, decimal amt, bool fee = false) => new() { AssetId = a.Id, Direction = LegDirection.Out, Amount = amt, IsFee = fee };

    private static Transaction Tx(TransactionType type, Guid acc, string at, string? externalId = null, TransactionStatus status = TransactionStatus.Ok, params TransactionLeg[] legs) =>
        new() { Id = Guid.NewGuid(), Type = type, TimestampUtc = T(at), AccountId = acc, Source = "t", ExternalId = externalId ?? $"x{++_n:D4}", Status = status, Legs = [.. legs] };

    private static Transaction BuyEur(Guid acc, string at, Asset a, decimal qty, decimal eur, string? id = null) => Tx(TransactionType.Buy, acc, at, id, TransactionStatus.Ok, In(a, qty), Out(Eur, eur));

    private static TaxEngineInput Input(IEnumerable<Transaction> txs, InMemoryPriceBook? prices = null, TaxSettings? settings = null) =>
        TaxEngineInput.Create([.. txs], [Btc, Eth, Eur, Usdt, Stock, Sol], prices, settings);

    private static IEnumerable<FindingKind> Kinds(TaxEngineResult r) => r.Findings.Select(f => f.Kind);

    // ---- Befunde statt stiller Annahmen ----

    [Fact]
    public void Eingang_ohne_Auszahlung_legt_kein_Lot_an_und_meldet_den_Befund()
    {
        var r = TaxCalculator.Calculate(Input([Tx(TransactionType.Deposit, A, "2025-01-01T10:00:00Z", legs: In(Btc, 1))]));

        Kinds(r).Should().Equal(FindingKind.UnmatchedTransferIn);
        r.Lots.Should().BeEmpty();
    }

    [Fact]
    public void Auszahlung_ohne_Gegenstueck_laesst_die_Lots_im_Quellkonto_und_schlaegt_eine_Zuordnung_vor()
    {
        var buy = BuyEur(A, "2025-01-01T10:00:00Z", Btc, 1, 10000);
        // Eingang mit zu großer Abweichung (5 %): unter der Toleranz, deshalb nur ein Vorschlag
        var r = TaxCalculator.Calculate(Input([buy,
            Tx(TransactionType.Withdrawal, A, "2025-02-01T10:00:00Z", legs: Out(Btc, 1)),
            Tx(TransactionType.Deposit, B, "2025-02-01T11:00:00Z", legs: In(Btc, 0.95m))]));

        r.Findings.Should().Contain(f => f.Kind == FindingKind.UnmatchedTransferOut && f.Message.Contains("Mögliches Gegenstück", StringComparison.Ordinal));
        r.Findings.Should().Contain(f => f.Kind == FindingKind.UnmatchedTransferIn);
        r.Holdings[(A, Btc.Id)].Should().Be(1);
    }

    [Fact]
    public void Buchung_zur_Pruefung_wird_gemeldet_und_nicht_verrechnet()
    {
        var r = TaxCalculator.Calculate(Input([Tx(TransactionType.Unknown, A, "2025-01-01T10:00:00Z", status: TransactionStatus.NeedsReview, legs: In(Btc, 1))]));

        Kinds(r).Should().Equal(FindingKind.TransactionNeedsReview);
        r.Lots.Should().BeEmpty();
    }

    [Fact]
    public void Ignorierte_Buchungen_werden_uebersprungen()
    {
        var r = TaxCalculator.Calculate(Input([Tx(TransactionType.Unknown, A, "2025-01-01T10:00:00Z", status: TransactionStatus.Ignored)]));

        r.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Schenkung_von_Krypto_braucht_Angaben_und_wird_nicht_geraten()
    {
        var r = TaxCalculator.Calculate(Input([Tx(TransactionType.GiftIn, A, "2025-01-01T10:00:00Z", legs: In(Btc, 1)),
            Tx(TransactionType.GiftIn, A, "2025-01-02T10:00:00Z", legs: In(Eur, 50))]));

        Kinds(r).Should().Equal(FindingKind.DonationNeedsData);
        r.Lots.Should().BeEmpty();
    }

    [Fact]
    public void Tausch_mit_Wertpapier_gegen_Krypto_wird_nicht_unterstuetzt()
    {
        var r = TaxCalculator.Calculate(Input([Tx(TransactionType.Trade, A, "2025-01-01T10:00:00Z", legs: [In(Btc, 1), Out(Stock, 5)])]));

        Kinds(r).Should().Equal(FindingKind.UnsupportedAsset);
    }

    [Fact]
    public void Wertpapier_gegen_EUR_gehoert_ins_Aktien_Modul_und_erzeugt_keinen_Befund()
    {
        var r = TaxCalculator.Calculate(Input([Tx(TransactionType.Buy, A, "2025-01-01T10:00:00Z", legs: [In(Stock, 5), Out(Eur, 500)]),
            Tx(TransactionType.Buy, A, "2025-01-02T10:00:00Z", legs: [In(Eur, 1), Out(Eur, 1)])]));

        r.Findings.Should().BeEmpty();
        r.Lots.Should().BeEmpty();
    }

    [Fact]
    public void Gebuehr_in_drittem_Coin_ohne_Kurs_stoppt_alle_beteiligten_Assets()
    {
        var r = TaxCalculator.Calculate(Input([
            BuyEur(A, "2025-01-01T10:00:00Z", Usdt, 1000, 900),
            BuyEur(A, "2025-01-01T10:01:00Z", Sol, 10, 100),
            Tx(TransactionType.Buy, A, "2025-02-01T10:00:00Z", legs: [In(Btc, 0.01m), Out(Usdt, 600), Out(Sol, 1, fee: true)]),
            BuyEur(A, "2025-02-02T10:00:00Z", Btc, 1, 50000)]));

        r.Findings.Select(f => f.Kind).Should().Contain(FindingKind.MissingPrice).And.Contain(FindingKind.AssetBlocked);
        r.Disposals.Should().BeEmpty();
        r.Lots.Count(l => l.AssetId == Btc.Id).Should().Be(0, "BTC ist gestoppt, auch der spätere Kauf wird übersprungen");
    }

    [Fact]
    public void Gebuehr_in_nicht_unterstuetztem_Asset_wird_gemeldet()
    {
        var r = TaxCalculator.Calculate(Input([BuyEur(A, "2025-01-01T10:00:00Z", Btc, 1, 10000),
            Tx(TransactionType.Sell, A, "2025-02-01T10:00:00Z", legs: [Out(Btc, 1), In(Eur, 20000), Out(Stock, 1, fee: true)])]));

        Kinds(r).Should().Equal(FindingKind.UnsupportedAsset);
    }

    [Fact]
    public void Gebuehr_im_erhaltenen_Coin_mindert_die_Menge_im_Lot()
    {
        var r = TaxCalculator.Calculate(Input([Tx(TransactionType.Buy, A, "2025-01-01T10:00:00Z", legs: [In(Btc, 1), Out(Eur, 10000), Out(Btc, 0.001m, fee: true)])]));

        r.Lots.Should().ContainSingle().Which.Should().Match<LotRecord>(l => l.Quantity == 0.999m && l.CostEur == 10000m);
    }

    [Fact]
    public void Gebuehr_im_verkauften_Coin_erhoeht_die_verbrauchte_Menge()
    {
        var r = TaxCalculator.Calculate(Input([BuyEur(A, "2025-01-01T10:00:00Z", Btc, 1, 10000),
            Tx(TransactionType.Sell, A, "2025-02-01T10:00:00Z", legs: [Out(Btc, 0.5m), In(Eur, 6000), Out(Btc, 0.01m, fee: true)])]));

        r.Disposals.Sum(d => d.Quantity).Should().Be(0.51m);
        r.Disposals.Sum(d => d.ProceedsEur).Should().Be(6000m);
        r.Holdings[(A, Btc.Id)].Should().Be(0.49m);
    }

    [Fact]
    public void Gebuehr_in_EUR_beim_Verkauf_mindert_den_Gewinn_und_beim_Kauf_erhoeht_sie_die_Kosten()
    {
        var r = TaxCalculator.Calculate(Input([
            Tx(TransactionType.Buy, A, "2025-01-01T10:00:00Z", legs: [In(Btc, 1), Out(Eur, 10000), Out(Eur, 10, fee: true)]),
            Tx(TransactionType.Sell, A, "2025-02-01T10:00:00Z", legs: [Out(Btc, 1), In(Eur, 12000), Out(Eur, 20, fee: true)])]));

        var d = r.Disposals.Single();
        d.CostEur.Should().Be(10010m);
        d.FeeEur.Should().Be(20m);
        d.GainEur.Should().Be(12000m - 10010m - 20m);
    }

    // ---- Bezahlen, Einkünfte ----

    [Fact]
    public void Bezahlen_mit_Krypto_ist_eine_Veraeusserung_zum_Marktwert()
    {
        var prices = new InMemoryPriceBook().Add(Btc.Id, T("2025-03-01T00:00:00Z"), 30000m, "test");
        var r = TaxCalculator.Calculate(Input([BuyEur(A, "2025-01-01T10:00:00Z", Btc, 1, 20000),
            Tx(TransactionType.Spend, A, "2025-03-01T12:00:00Z", legs: Out(Btc, 0.1m))], prices));

        var d = r.Disposals.Single();
        d.ProceedsEur.Should().Be(3000m);
        d.CostEur.Should().Be(2000m);
        d.PriceSource.Should().Be("test");
    }

    [Fact]
    public void Bezahlen_mit_Krypto_ohne_Kurs_meldet_den_Befund_und_stoppt_das_Asset()
    {
        var r = TaxCalculator.Calculate(Input([BuyEur(A, "2025-01-01T10:00:00Z", Btc, 1, 20000), Tx(TransactionType.Spend, A, "2025-03-01T12:00:00Z", legs: Out(Btc, 0.1m))]));

        Kinds(r).Should().Contain(FindingKind.MissingPrice);
        r.Disposals.Should().BeEmpty();
    }

    [Fact]
    public void Einkunftsarten_Lending_Interest_Mining_Bonus_und_Airdrop_mit_Gegenleistung()
    {
        var prices = new InMemoryPriceBook().Add(Sol.Id, T("2025-03-01T00:00:00Z"), 100m, "p");
        var settings = new TaxSettings { Airdrops = AirdropTreatment.WithConsideration };
        var r = TaxCalculator.Calculate(Input([
            Tx(TransactionType.Lending, A, "2025-03-01T10:00:00Z", legs: In(Sol, 1)),
            Tx(TransactionType.Interest, A, "2025-03-01T11:00:00Z", legs: In(Sol, 1)),
            Tx(TransactionType.Mining, A, "2025-03-01T12:00:00Z", legs: In(Sol, 1)),
            Tx(TransactionType.Bonus, A, "2025-03-01T13:00:00Z", legs: In(Sol, 1)),
            Tx(TransactionType.Airdrop, A, "2025-03-01T14:00:00Z", legs: In(Sol, 1)),
            Tx(TransactionType.Interest, A, "2025-03-01T15:00:00Z", legs: In(Eur, 5))], prices, settings));

        r.Incomes.Select(i => i.Kind).Should().Equal(IncomeKind.Lending, IncomeKind.Interest, IncomeKind.Mining, IncomeKind.Bonus, IncomeKind.AirdropWithConsideration);
        r.Incomes.Should().OnlyContain(i => i.CountsAsIncome && i.ValueEur == 100m);
        r.Findings.Should().ContainSingle(f => f.Kind == FindingKind.MiningHint);
        r.Lots.Should().HaveCount(5).And.OnlyContain(l => l.CostEur == 100m);
    }

    [Fact]
    public void Airdrop_ohne_Gegenleistung_ist_keine_Einnahme_und_hat_Kosten_0_aber_Fristbeginn()
    {
        var r = TaxCalculator.Calculate(Input([Tx(TransactionType.Airdrop, A, "2025-03-01T10:00:00Z", legs: In(Sol, 5))]));

        r.Incomes.Should().ContainSingle().Which.Should().Match<IncomeRecord>(i => !i.CountsAsIncome && i.ValueEur == 0m && i.Kind == IncomeKind.AirdropWithoutConsideration);
        r.Lots.Should().ContainSingle().Which.Should().Match<LotRecord>(l => l.CostEur == 0m && l.Kind == LotKind.Airdrop);
        var year = YearlyAggregator.Aggregate(Input([]), r, new YearInputs(2025));
        year.Section22.IncomeEur.Should().Be(0m);
    }

    [Fact]
    public void Zufluss_ohne_Kurs_erzeugt_Befund_und_kein_Lot()
    {
        var r = TaxCalculator.Calculate(Input([Tx(TransactionType.Staking, A, "2025-03-01T10:00:00Z", legs: In(Sol, 1))]));

        Kinds(r).Should().Equal(FindingKind.MissingPrice);
        r.Lots.Should().BeEmpty();
        r.Incomes.Should().BeEmpty();
    }

    // ---- Futures ----

    [Fact]
    public void Futures_ohne_USDT_Kurs_ergeben_eine_Sammelmeldung_je_Coin()
    {
        var r = TaxCalculator.Calculate(Input([
            Tx(TransactionType.FundingFee, A, "2025-03-01T10:00:00Z", legs: Out(Usdt, 1)),
            Tx(TransactionType.FundingFee, A, "2025-03-05T10:00:00Z", legs: Out(Usdt, 1)),
            Tx(TransactionType.Fee, A, "2025-03-06T10:00:00Z", legs: Out(Eur, 5))]));

        r.Futures.Should().BeEmpty();
        r.Findings.Should().ContainSingle().Which.Message.Should().Contain("2 Futures-Buchungen").And.Contain("2025-03-01").And.Contain("2025-03-05");
    }

    // ---- Transfers: Reihenfolge und Netzwerkgebühr als Veräußerung ----

    [Fact]
    public void Eingang_mit_gleichem_Zeitstempel_vor_der_Auszahlung_wird_zurueckgestellt_und_danach_gebucht()
    {
        var buy = BuyEur(A, "2025-01-01T10:00:00Z", Btc, 1, 10000, "a-buy");
        var r = TaxCalculator.Calculate(Input([buy,
            Tx(TransactionType.Deposit, B, "2025-02-01T10:00:00Z", "a-deposit", TransactionStatus.Ok, In(Btc, 1)),
            Tx(TransactionType.Withdrawal, A, "2025-02-01T10:00:00Z", "b-withdrawal", TransactionStatus.Ok, Out(Btc, 1))]));

        r.Transfers.Should().ContainSingle();
        r.Findings.Should().BeEmpty();
        r.Holdings[(B, Btc.Id)].Should().Be(1);
        r.Holdings[(A, Btc.Id)].Should().Be(0);
        r.Lots.Single(l => l.AccountId == B).AcquiredAtUtc.Should().Be(T("2025-01-01T10:00:00Z"), "das Anschaffungsdatum wandert mit");
    }

    [Fact]
    public void Auszahlung_ueber_den_Bestand_meldet_negativen_Bestand_und_stoppt_das_Asset()
    {
        var r = TaxCalculator.Calculate(Input([BuyEur(A, "2025-01-01T10:00:00Z", Btc, 1, 10000),
            Tx(TransactionType.Withdrawal, A, "2025-02-01T10:00:00Z", legs: Out(Btc, 2)),
            Tx(TransactionType.Deposit, B, "2025-02-01T11:00:00Z", legs: In(Btc, 2))]));

        Kinds(r).Should().Contain(FindingKind.NegativeHolding);
        r.Transfers.Should().BeEmpty();
    }

    [Fact]
    public void Netzwerkgebuehr_als_Veraeusserung_Option_b_erzeugt_ein_Disposal_zum_Marktwert()
    {
        var prices = new InMemoryPriceBook().Add(Btc.Id, T("2025-02-01T00:00:00Z"), 30000m, "p");
        var settings = new TaxSettings { NetworkFees = NetworkFeeTreatment.DisposalAtMarketValue };
        var r = TaxCalculator.Calculate(Input([BuyEur(A, "2025-01-01T10:00:00Z", Btc, 1, 20000),
            Tx(TransactionType.Withdrawal, A, "2025-02-01T10:00:00Z", legs: [Out(Btc, 0.999m), Out(Btc, 0.001m, fee: true)]),
            Tx(TransactionType.Deposit, B, "2025-02-01T11:00:00Z", legs: In(Btc, 0.999m))], prices, settings));

        var d = r.Disposals.Single();
        d.Quantity.Should().Be(0.001m);
        d.ProceedsEur.Should().Be(30m);
        d.CostEur.Should().Be(20m);
        d.GainEur.Should().Be(10m);
        d.Note.Should().Contain("Option b");
        r.NetworkFees.Single().Treatment.Should().Be(TaxTreatment.Section23Taxable);
        r.NetworkFees.Single().ValueEur.Should().Be(30m);
        r.Lots.Single(l => l.AccountId == B).CostEur.Should().Be(19980m);
    }

    [Fact]
    public void Netzwerkgebuehr_als_Veraeusserung_ohne_Kurs_meldet_den_Befund()
    {
        var settings = new TaxSettings { NetworkFees = NetworkFeeTreatment.DisposalAtMarketValue };
        var r = TaxCalculator.Calculate(Input([BuyEur(A, "2025-01-01T10:00:00Z", Btc, 1, 20000),
            Tx(TransactionType.Withdrawal, A, "2025-02-01T10:00:00Z", legs: [Out(Btc, 0.999m), Out(Btc, 0.001m, fee: true)]),
            Tx(TransactionType.Deposit, B, "2025-02-01T11:00:00Z", legs: In(Btc, 0.999m))], settings: settings));

        Kinds(r).Should().Contain(FindingKind.MissingPrice);
    }

    [Fact]
    public void Transfer_Zuordnung_waehlt_bei_mehreren_Kandidaten_den_mit_der_hoechsten_Konfidenz()
    {
        var matcher = TransferMatcher.Match(
        [
            Tx(TransactionType.Withdrawal, A, "2025-02-01T10:00:00Z", "w", TransactionStatus.Ok, Out(Btc, 1)),
            Tx(TransactionType.Deposit, B, "2025-02-01T10:30:00Z", "d-ungenau", TransactionStatus.Ok, In(Btc, 0.99m)),
            Tx(TransactionType.Deposit, B, "2025-02-02T09:00:00Z", "d-genau-spaet", TransactionStatus.Ok, In(Btc, 1m)),
            Tx(TransactionType.Deposit, A, "2025-02-01T10:05:00Z", "d-gleiches-konto", TransactionStatus.Ok, In(Btc, 1m)),
        ], new Dictionary<Guid, Asset> { [Btc.Id] = Btc }, new TaxSettings());

        matcher.Matches.Should().ContainSingle().Which.In.ExternalId.Should().Be("d-genau-spaet", "gleiche Menge zählt mehr als der kürzere Zeitabstand");
        matcher.Matches[0].Confidence.Should().BeInRange(0.6m, 1m);
        matcher.UnmatchedIn.Select(t => t.ExternalId).Should().BeEquivalentTo("d-ungenau", "d-gleiches-konto");
    }

    // ---- Regelsatz, Kurse, Modelle ----

    [Fact]
    public void Regelsatz_beschreibt_den_gueltigen_Parameter_je_Jahr()
    {
        var p = RuleSetCatalog.DeEStG23.Describe("Freigrenze23", 2023);

        p.Value.Should().Be(600);
        p.LegalBasis.Should().Contain("§ 23");
    }

    [Fact]
    public void Kurse_die_aelter_als_die_Toleranz_sind_gelten_nicht()
    {
        var book = new InMemoryPriceBook(TimeSpan.FromDays(2)).Add(Btc.Id, T("2025-03-01T00:00:00Z"), 1m, "x");

        book.Find(Btc.Id, T("2025-03-02T00:00:00Z")).Should().NotBeNull();
        book.Find(Btc.Id, T("2025-03-04T00:00:00Z")).Should().BeNull();
        book.Find(Btc.Id, T("2025-02-28T00:00:00Z")).Should().BeNull("der Kurs liegt in der Zukunft");
        book.Find(Eth.Id, T("2025-03-02T00:00:00Z")).Should().BeNull();
    }

    [Fact]
    public void Jahresaggregation_weist_Aufschluesselung_Futures_und_Netzwerkgebuehren_aus()
    {
        var prices = new InMemoryPriceBook().Add(Usdt.Id, T("2025-03-01T00:00:00Z"), 0.9m, "ecb");
        var input = Input([
            BuyEur(A, "2025-01-01T10:00:00Z", Btc, 2, 20000),
            Tx(TransactionType.Sell, A, "2025-02-01T10:00:00Z", legs: [Out(Btc, 1), In(Eur, 15000)]),
            Tx(TransactionType.Withdrawal, A, "2025-03-01T10:00:00Z", legs: [Out(Btc, 0.99m), Out(Btc, 0.01m, fee: true)]),
            Tx(TransactionType.Deposit, B, "2025-03-01T11:00:00Z", legs: In(Btc, 0.99m)),
            Tx(TransactionType.FuturesPnL, A, "2025-03-02T10:00:00Z", legs: In(Usdt, 10))], prices);
        var result = TaxCalculator.Calculate(input);

        var year = YearlyAggregator.Aggregate(input, result, new YearInputs(2025, OtherPrivateSalesGainEur: 100m, Section22ExpensesEur: 5m));
        var fees = YearlyAggregator.NetworkFees(result, 2025);

        year.Section23.ByAssetAndAccount.Should().ContainSingle().Which.Should().Match<AssetResult>(a => a.AccountId == A && a.AssetId == Btc.Id && a.NetEur == 5000m && a.ProceedsEur == 15000m && a.CostEur == 10000m);
        year.Section23.OtherPrivateSalesEur.Should().Be(100m);
        year.Section23.TotalGainEur.Should().Be(5100m);
        year.Futures.NetEur.Should().Be(9m);
        year.LawRuleSet.Should().Be("DE-23EStG");
        year.ReformRuleSet.Should().BeNull();
        year.Reform.Should().BeNull();
        fees.Transfers.Should().Be(1);
        fees.QuantityByAsset[Btc.Id].Should().Be(0.01m);
        result.Transfers.Single().Should().Match<TransferRecord>(t => t.NetworkFeeQuantity == 0.01m && t.FromAccountId == A && t.ToAccountId == B && t.Confidence > 0);
        result.Lots.Should().OnlyContain(l => l.TaxFreeFrom > DateOnly.FromDateTime(l.AcquiredAtUtc.DateTime));
    }

    [Fact]
    public void Reform_Neu_Einkuenfte_werden_im_Kapitalertragstopf_und_nicht_in_Paragraph_22_gezaehlt()
    {
        var prices = new InMemoryPriceBook().Add(Sol.Id, T("2027-03-01T00:00:00Z"), 100m, "p");
        var settings = new TaxSettings { ApplyReform2027 = true };
        var input = Input([Tx(TransactionType.Staking, A, "2027-03-01T10:00:00Z", legs: In(Sol, 20))], prices, settings);
        var result = TaxCalculator.Calculate(input);

        var year = YearlyAggregator.Aggregate(input, result, new YearInputs(2027, JointAssessment: true));

        result.Lots.Single().Regime.Should().Be(TaxRegime.Neu);
        year.Section22.IncomeEur.Should().Be(0m);
        year.Reform!.NetCapitalGainEur.Should().Be(2000m);
        year.Reform.PauschbetragEur.Should().Be(2000m, "Zusammenveranlagung");
        year.Reform.TotalTaxEur.Should().Be(0m);
        year.ReformRuleSet.Should().Be("DE-Reform-2027-Entwurf");
    }

    [Fact]
    public void Tauschkrypto_Ausnahme_haelt_ausgenommene_Assets_im_Regime_Alt()
    {
        var settings = new TaxSettings { NotTauschkrypto = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BTC" } };
        var r = TaxCalculator.Calculate(Input([BuyEur(A, "2027-03-01T10:00:00Z", Btc, 1, 10000), BuyEur(A, "2027-03-01T10:00:01Z", Eth, 1, 1000)], settings: settings));

        r.Lots.Single(l => l.AssetId == Btc.Id).Regime.Should().Be(TaxRegime.Alt);
        r.Lots.Single(l => l.AssetId == Eth.Id).Regime.Should().Be(TaxRegime.Neu);
    }
}
