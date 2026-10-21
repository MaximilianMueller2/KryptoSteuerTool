using KryptoSteuer.Infrastructure.Secrets;
using Microsoft.AspNetCore.DataProtection;

namespace KryptoSteuer.Api.IntegrationTests;

public class SecretProtectorTests
{
    private static DataProtectionSecretProtector Create(IDataProtectionProvider? provider = null) =>
        new(provider ?? new EphemeralDataProtectionProvider());

    [Fact]
    public void Protect_liefert_nicht_den_Klartext_und_Unprotect_stellt_ihn_wieder_her()
    {
        var protector = Create();

        var cipher = protector.Protect("bitget-api-key-123");

        cipher.Should().NotContain("bitget-api-key-123");
        protector.Unprotect(cipher).Should().Be("bitget-api-key-123");
    }

    [Fact]
    public void Unprotect_scheitert_mit_einem_anderen_Schluesselring()
    {
        var cipher = Create().Protect("geheim");

        var act = () => Create().Unprotect(cipher);

        act.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void Unprotect_scheitert_bei_manipuliertem_Wert()
    {
        var protector = Create();
        var cipher = protector.Protect("geheim");

        var act = () => protector.Unprotect(cipher[..^2] + "AA");

        act.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }
}
