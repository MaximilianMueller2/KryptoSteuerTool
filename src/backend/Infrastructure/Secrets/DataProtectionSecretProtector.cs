using KryptoSteuer.Application.Secrets;
using Microsoft.AspNetCore.DataProtection;

namespace KryptoSteuer.Infrastructure.Secrets;

/// <summary>
/// Verschlüsselt mit ASP.NET Data Protection. Der Schlüsselring liegt außerhalb der Datenbank
/// (siehe docs/07-betrieb.md), damit ein DB-Backup allein keine Börsen-Keys preisgibt.
/// </summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public const string Purpose = "KryptoSteuer.ExchangeCredentials.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedValue);
        return _protector.Unprotect(protectedValue);
    }
}
