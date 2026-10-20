using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Application.Imports;

/// <summary>
/// Löst Symbole zu Assets auf. Unbekannte Symbole werden im Speicher angelegt und erst beim
/// Festschreiben gespeichert; eine Vorschau verändert die Datenbank also nicht.
/// </summary>
public sealed class AssetCatalog(IEnumerable<Asset> existing)
{
    private static readonly HashSet<string> Fiat = new(StringComparer.OrdinalIgnoreCase) { "EUR", "USD", "GBP", "CHF" };

    private static readonly HashSet<string> Stablecoins = new(StringComparer.OrdinalIgnoreCase)
        { "USDT", "USDC", "DAI", "BUSD", "TUSD", "FDUSD", "USDE", "PYUSD" };

    private readonly Dictionary<string, Asset> _bySymbol = existing
        .GroupBy(a => a.Symbol, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private readonly List<Asset> _created = [];

    public IReadOnlyList<Asset> NewAssets => _created;

    public Guid Resolve(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var key = symbol.Trim();
        if (_bySymbol.TryGetValue(key, out var asset))
            return asset.Id;

        asset = new Asset { Symbol = key.ToUpperInvariant(), Class = Classify(key) };
        _bySymbol[key] = asset;
        _created.Add(asset);
        return asset.Id;
    }

    private static AssetClass Classify(string symbol) =>
        Fiat.Contains(symbol) ? AssetClass.Fiat
        : Stablecoins.Contains(symbol) ? AssetClass.Stablecoin
        : AssetClass.Crypto;
}
