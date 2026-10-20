using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Domain.Tests;

public class IdempotencyKeyTests
{
    [Fact]
    public void Mit_externer_ID_zaehlt_nur_Quelle_und_ID()
    {
        var a = IdempotencyKey.For("bitget", "123", "zeile A");
        var b = IdempotencyKey.For("bitget", "123", "zeile B");

        a.Should().Be(b);
        a.Should().Be("bitget:123");
    }

    [Fact]
    public void Gleiche_ID_in_anderer_Quelle_ist_ein_anderer_Schluessel()
    {
        IdempotencyKey.For("bitget", "123", "x").Should().NotBe(IdempotencyKey.For("traderepublic", "123", "x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Ohne_externe_ID_zaehlt_der_Hash_des_Rohinhalts(string? externalId)
    {
        var a = IdempotencyKey.For("tr", externalId, "zeile A");

        a.Should().Be(IdempotencyKey.For("tr", externalId, "zeile A"));
        a.Should().NotBe(IdempotencyKey.For("tr", externalId, "zeile B"));
        a.Should().StartWith("tr:sha256:");
    }

    [Fact]
    public void Sha256_liefert_den_bekannten_Hash()
    {
        IdempotencyKey.Sha256Hex("abc")
            .Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void RawRecord_und_Transaction_nutzen_denselben_Schluessel()
    {
        var raw = new RawRecord
        {
            Source = "bitget", ExternalId = "42", Payload = "{}", FileHash = "h",
            ImportRunId = Guid.NewGuid(), ImportedAtUtc = DateTimeOffset.UnixEpoch,
        };
        var tx = new Transaction
        {
            Type = TransactionType.Deposit, TimestampUtc = DateTimeOffset.UnixEpoch,
            AccountId = Guid.NewGuid(), Source = "bitget", ExternalId = "42",
        };

        tx.DedupKey.Should().Be(raw.DedupKey);
    }
}
