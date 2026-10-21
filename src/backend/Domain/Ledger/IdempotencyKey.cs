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
            ? FromHash(source, null, Sha256Hex(rawContent))
            : FromHash(source, externalId, string.Empty);
    }

    /// <summary>Wie <see cref="For"/>, aber mit bereits berechnetem Inhalts-Hash.</summary>
    public static string FromHash(string source, string? externalId, string contentHash) =>
        string.IsNullOrWhiteSpace(externalId)
            ? $"{source}:sha256:{contentHash}"
            : $"{source}:{externalId.Trim()}";
}
