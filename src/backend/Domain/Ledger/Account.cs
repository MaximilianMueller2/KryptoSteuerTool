namespace KryptoSteuer.Domain.Ledger;

/// <summary>Eine Wallet, ein Börsenkonto oder ein Depot.</summary>
public sealed class Account
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required AccountType Type { get; set; }
    /// <summary>z. B. Bitget, TradeRepublic.</summary>
    public required string Platform { get; set; }
    /// <summary>ISO-3166-Ländercode der Plattform, z. B. "DE", "SC".</summary>
    public string? Country { get; set; }
    /// <summary>Teilkonto, z. B. "Spot", "Futures", "Earn".</summary>
    public string? SubAccount { get; set; }
}
