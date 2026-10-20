using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Engine;
using KryptoSteuer.TaxEngine.Model;
using KryptoSteuer.TaxEngine.Pricing;

namespace KryptoSteuer.TaxEngine.Tests;

/// <summary>Entscheidungen des Nutzers (Review-Queue): Transfer auf ein eigenes Konto und bestätigte Prüfbuchungen.</summary>
public class DecisionTests
{
    private static readonly Asset Sol = new() { Id = GoldenScenario.AssetId("SOL"), Symbol = "SOL", Class = AssetClass.Crypto };
    private static readonly Asset Eur = new() { Id = GoldenScenario.AssetId("EUR"), Symbol = "EUR", Class = AssetClass.Fiat };
    private static readonly Guid Tr = GoldenScenario.AccountId("TR");
    private static readonly Guid Wallet = GoldenScenario.AccountId("Wallet");
    private static int _n;

    private static DateTimeOffset T(string s) => DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);

    private static Transaction Tx(TransactionType type, Guid acc, string at, TransactionStatus status = TransactionStatus.Ok, params TransactionLeg[] legs) =>
        new() { Id = Guid.NewGuid(), Type = type, TimestampUtc = T(at), AccountId = acc, Source = "t", ExternalId = $"d{++_n:D3}", Status = status, Legs = [.. legs] };

    private static TransactionLeg In(Asset a, decimal amt) => new() { AssetId = a.Id, Direction = LegDirection.In, Amount = amt };
    private static TransactionLeg Out(Asset a, decimal amt) => new() { AssetId = a.Id, Direction = LegDirection.Out, Amount = amt };

    private static (Transaction Buy, Transaction Withdrawal) Setup() =>
        (Tx(TransactionType.Buy, Tr, "2025-01-10T10:00:00Z", legs: [In(Sol, 2), Out(Eur, 300)]),
         Tx(TransactionType.Withdrawal, Tr, "2025-03-01T10:00:00Z", legs: Out(Sol, 2)));

    private static TaxEngineInput Input(IEnumerable<Transaction> txs, EngineDecisions? decisions = null) =>
        new(txs.ToList(), new[] { Sol, Eur }.ToDictionary(a => a.Id), new InMemoryPriceBook(),
            KryptoSteuer.TaxEngine.Rules.RuleSetCatalog.DeEStG23, KryptoSteuer.TaxEngine.Rules.RuleSetCatalog.DeReform2027, new KryptoSteuer.TaxEngine.Rules.TaxSettings())
        { Decisions = decisions ?? EngineDecisions.None };

    [Fact]
    public void Ohne_Entscheidung_bleibt_die_Auszahlung_ein_Befund()
    {
        var (buy, w) = Setup();
        var r = TaxCalculator.Calculate(Input([buy, w]));

        r.Findings.Should().ContainSingle(f => f.Kind == FindingKind.UnmatchedTransferOut);
        r.Holdings[(Tr, Sol.Id)].Should().Be(2);
    }

    [Fact]
    public void Transfer_auf_eigenes_Konto_verschiebt_die_Lots_mit_Anschaffungsdatum_und_Kosten()
    {
        var (buy, w) = Setup();
        var r = TaxCalculator.Calculate(Input([buy, w], new EngineDecisions(new Dictionary<Guid, Guid> { [w.Id] = Wallet }, new HashSet<Guid>())));

        r.Findings.Should().BeEmpty();
        r.Transfers.Should().ContainSingle(t => t.ToAccountId == Wallet && t.OutQuantity == 2);
        r.Holdings.GetValueOrDefault((Tr, Sol.Id)).Should().Be(0);
        r.Holdings[(Wallet, Sol.Id)].Should().Be(2);
        var moved = r.Lots.Single(l => l.AccountId == Wallet);
        moved.AcquiredAtUtc.Should().Be(T("2025-01-10T10:00:00Z"));
        moved.CostEur.Should().Be(300);
        r.Disposals.Should().BeEmpty();
    }

    [Fact]
    public void Verkauf_nach_dem_Transfer_behaelt_die_urspruengliche_Haltefrist()
    {
        var (buy, w) = Setup();
        var sell = Tx(TransactionType.Sell, Wallet, "2025-06-01T10:00:00Z", legs: [Out(Sol, 2), In(Eur, 400)]);
        var r = TaxCalculator.Calculate(Input([buy, w, sell], new EngineDecisions(new Dictionary<Guid, Guid> { [w.Id] = Wallet }, new HashSet<Guid>())));

        var d = r.Disposals.Should().ContainSingle().Subject;
        d.CostEur.Should().Be(300);
        d.GainEur.Should().Be(100);
        d.HoldingDays.Should().Be(142);
    }

    [Fact]
    public void Entscheidung_auf_dasselbe_Konto_wird_ignoriert()
    {
        var (buy, w) = Setup();
        var r = TaxCalculator.Calculate(Input([buy, w], new EngineDecisions(new Dictionary<Guid, Guid> { [w.Id] = Tr }, new HashSet<Guid>())));

        r.Findings.Should().ContainSingle(f => f.Kind == FindingKind.UnmatchedTransferOut);
    }

    [Fact]
    public void Bestaetigte_Pruefbuchung_erzeugt_keinen_Befund_und_wird_nicht_verrechnet()
    {
        var review = Tx(TransactionType.Unknown, Tr, "2025-02-01T10:00:00Z", TransactionStatus.NeedsReview, In(Sol, 1));
        var open = TaxCalculator.Calculate(Input([review]));
        var acknowledged = TaxCalculator.Calculate(Input([review], new EngineDecisions(new Dictionary<Guid, Guid>(), new HashSet<Guid> { review.Id })));

        open.Findings.Should().ContainSingle(f => f.Kind == FindingKind.TransactionNeedsReview);
        acknowledged.Findings.Should().BeEmpty();
        acknowledged.Lots.Should().BeEmpty();
    }
}
