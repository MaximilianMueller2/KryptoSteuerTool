namespace KryptoSteuer.Domain.Ledger;

/// <summary>
/// API-Zugang zu einer Börse für ein Konto. Key, Secret und Passphrase liegen nur verschlüsselt
/// (ASP.NET Data Protection) in der Datenbank; der Schlüsselring liegt außerhalb (docs/07-betrieb.md).
/// </summary>
public sealed class ExchangeCredential
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid AccountId { get; init; }
    /// <summary>z. B. "bitget".</summary>
    public required string Provider { get; init; }
    public required string EncryptedApiKey { get; set; }
    public required string EncryptedSecret { get; set; }
    public required string EncryptedPassphrase { get; set; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    /// <summary>"classic" oder "uta".</summary>
    public string? AccountType { get; set; }
    /// <summary>Warnungen der Rechteprüfung, getrennt durch Zeilenumbruch.</summary>
    public string? PermissionWarnings { get; set; }
    public DateTimeOffset? LastSyncAtUtc { get; set; }
    public string? LastSyncSummary { get; set; }
}
