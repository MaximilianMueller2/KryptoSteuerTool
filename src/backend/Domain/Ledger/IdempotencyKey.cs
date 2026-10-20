using System.Security.Cryptography;
using System.Text;

namespace KryptoSteuer.Domain.Ledger;

/// <summary>
/// Eindeutigkeitsschlüssel für Importe: (Quelle, externe ID); ohne externe ID der Hash des Rohinhalts.
/// </summary>
public static class IdempotencyKey
{
    public static string Sha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    public static string For(string source, string? externalId, string rawContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return string.IsNullOrWhiteSpace(externalId)
            ? $"{source}:sha256:{Sha256Hex(rawContent)}"
            : $"{source}:{externalId.Trim()}";
    }
}
