namespace KryptoSteuer.Domain.Ledger;

public sealed class Asset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Symbol { get; set; }
    public string? Name { get; set; }
    public required AssetClass Class { get; set; }
    public string? Isin { get; set; }
    public string? ContractAddress { get; set; }
    public string? Chain { get; set; }
    /// <summary>Tauschkryptowert im Sinne des Reform-Entwurfs (für den abschaltbaren Regelsatz).</summary>
    public bool IsTauschkryptowert { get; set; }
    /// <summary>Wrapped-Token (z. B. wBTC); <see cref="WrappedOfAssetId"/> verweist auf das Original.</summary>
    public bool IsWrappedOf { get; set; }
    public Guid? WrappedOfAssetId { get; set; }
}
