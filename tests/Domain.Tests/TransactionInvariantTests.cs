using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Domain.Tests;

public class TransactionInvariantTests
{
    private static readonly Guid Btc = Guid.NewGuid();
    private static readonly Guid Eur = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static TransactionLeg Leg(LegDirection direction, decimal amount = 1m, bool fee = false) =>
        new() { AssetId = Btc, Amount = amount, Direction = direction, IsFee = fee };

    private static Transaction Tx(TransactionType type, DateTimeOffset? at = null, params TransactionLeg[] legs) =>
        new()
        {
            Type = type,
            TimestampUtc = at ?? Now,
            AccountId = Guid.NewGuid(),
            Source = "test",
            ExternalId = "1",
            Legs = [.. legs],
        };

    [Fact]
    public void Trade_mit_einem_In_und_einem_Out_Bein_ist_gueltig()
    {
        var tx = Tx(TransactionType.Trade, null, Leg(LegDirection.In), Leg(LegDirection.Out, 2m));

        tx.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Trade_mit_Gebuehrenbein_ist_gueltig()
    {
        var tx = Tx(TransactionType.Trade, null,
            Leg(LegDirection.In), Leg(LegDirection.Out, 2m), Leg(LegDirection.Out, 0.01m, fee: true));

        tx.Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    public void Trade_braucht_genau_ein_In_und_ein_Out_Bein(int ins, int outs)
    {
        var legs = Enumerable.Repeat(Leg(LegDirection.In), ins)
            .Concat(Enumerable.Repeat(Leg(LegDirection.Out), outs))
            .ToArray();

        Tx(TransactionType.Trade, null, legs).Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void Zwei_Gebuehrenbeine_sind_ungueltig()
    {
        var tx = Tx(TransactionType.Trade, null,
            Leg(LegDirection.In), Leg(LegDirection.Out),
            Leg(LegDirection.Out, 0.1m, fee: true), Leg(LegDirection.Out, 0.1m, fee: true));

        tx.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void Gebuehrenbein_muss_ausgehend_sein()
    {
        var tx = Tx(TransactionType.Deposit, null, Leg(LegDirection.In), Leg(LegDirection.In, 0.1m, fee: true));

        tx.Validate().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Menge_muss_positiv_sein(double amount)
    {
        var tx = Tx(TransactionType.Deposit, null, Leg(LegDirection.In, (decimal)amount));

        tx.Validate().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(TransactionType.Deposit)]
    [InlineData(TransactionType.TransferIn)]
    [InlineData(TransactionType.Staking)]
    [InlineData(TransactionType.Airdrop)]
    [InlineData(TransactionType.Interest)]
    public void Zugangstypen_haben_genau_ein_In_Bein(TransactionType type)
    {
        Tx(type, null, Leg(LegDirection.In)).Validate().Should().BeEmpty();
        Tx(type, null, Leg(LegDirection.Out)).Validate().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(TransactionType.Withdrawal)]
    [InlineData(TransactionType.TransferOut)]
    [InlineData(TransactionType.Spend)]
    [InlineData(TransactionType.GiftOut)]
    [InlineData(TransactionType.Fee)]
    public void Abgangstypen_haben_genau_ein_Out_Bein(TransactionType type)
    {
        Tx(type, null, Leg(LegDirection.Out)).Validate().Should().BeEmpty();
        Tx(type, null, Leg(LegDirection.In)).Validate().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(TransactionType.FuturesPnL)]
    [InlineData(TransactionType.FundingFee)]
    [InlineData(TransactionType.Liquidation)]
    public void Futures_Typen_haben_genau_ein_Bein_in_beliebiger_Richtung(TransactionType type)
    {
        Tx(type, null, Leg(LegDirection.In)).Validate().Should().BeEmpty();
        Tx(type, null, Leg(LegDirection.Out)).Validate().Should().BeEmpty();
        Tx(type, null, Leg(LegDirection.In), Leg(LegDirection.Out)).Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void Zeitstempel_muss_UTC_sein()
    {
        var berlin = new DateTimeOffset(2026, 3, 1, 13, 0, 0, TimeSpan.FromHours(1));

        Tx(TransactionType.Deposit, berlin, Leg(LegDirection.In)).Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void EnsureValid_wirft_bei_Verstoessen()
    {
        var tx = Tx(TransactionType.Trade, null, Leg(LegDirection.In));

        var act = tx.EnsureValid;

        act.Should().Throw<InvalidOperationException>();
    }
}
