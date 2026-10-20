using KryptoSteuer.Importers.Bitget;

namespace KryptoSteuer.Importers.Tests.Bitget;

public class BitgetSignerTests
{
    // Erwartete Werte unabhängig berechnet: base64(HMAC-SHA256("test-secret", Nachricht)).
    [Fact]
    public void GET_mit_Query_signiert_Zeitstempel_Methode_Pfad_und_Query()
    {
        var sign = BitgetSigner.Sign("test-secret", 1700000000000, "GET", "/api/v2/spot/account/assets", "coin=BTC&limit=100", "");

        sign.Should().Be("Ai+Hnqp8Tvrd+vIvheHwDCV78yrvv6qEWa+Fkk1aRpg=");
    }

    [Fact]
    public void Query_darf_mit_oder_ohne_Fragezeichen_uebergeben_werden()
    {
        BitgetSigner.Sign("test-secret", 1700000000000, "GET", "/api/v2/spot/account/assets", "?coin=BTC&limit=100", "")
            .Should().Be("Ai+Hnqp8Tvrd+vIvheHwDCV78yrvv6qEWa+Fkk1aRpg=");
    }

    [Fact]
    public void POST_signiert_den_Body_ohne_Query()
    {
        BitgetSigner.Sign("test-secret", 1700000000000, "post", "/api/v2/spot/trade/place-order", "", "{\"a\":1}")
            .Should().Be("Ca194aZqc7deTROKSB1OK/+GmkCS1Xc0GJL5g/bKkvU=");
    }

    [Fact]
    public void Ohne_Query_wird_kein_Fragezeichen_angehaengt()
    {
        BitgetSigner.Sign("test-secret", 1700000000000, "GET", "/api/v2/public/time", "", "")
            .Should().Be("6Osbz6pW+nkTgqWONaQdfcq4g9+1+uD3I7VKV8lYDSw=");
    }
}
