namespace KryptoSteuer.Application.Secrets;

/// <summary>Port: verschlüsselt Geheimnisse (z. B. Börsen-API-Keys) vor dem Speichern in der Datenbank.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>Wirft <see cref="System.Security.Cryptography.CryptographicException"/>, wenn der Schlüssel fehlt oder der Wert manipuliert ist.</summary>
    string Unprotect(string protectedValue);
}
