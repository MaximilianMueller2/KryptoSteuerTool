using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Time;

namespace KryptoSteuer.Application.Prices;

/// <summary>Port: Kurse und Assets für den Kursdienst. Implementiert in Infrastructure.</summary>
public interface IPriceStore
{
    Task<IReadOnlyList<Asset>> GetAssetsAsync(CancellationToken cancellationToken);

    /// <summary>Speichert Kurse; (Asset, Zeitpunkt, Quelle) ist eindeutig, vorhandene werden aktualisiert. Liefert die Anzahl neuer oder geänderter Kurse.</summary>
    Task<int> UpsertAsync(IReadOnlyCollection<Price> prices, CancellationToken cancellationToken);

    Task<IReadOnlyList<Price>> ListAsync(Guid? assetId, CancellationToken cancellationToken);
}

public sealed record EcbImportResult(int Rates, IReadOnlyList<string> Assets, int PricesStored, string? Warning);

/// <summary>
/// Kursdienst für USD-Stablecoins: EUR-Wert über die EZB-Referenzkurse (USDT gleich USD). Der Kurs für ein Berliner Datum gilt für alle
/// Buchungen dieses Tages; an Wochenenden und Feiertagen gilt der zuletzt veröffentlichte (höchstens fünf Tage, siehe Preisliste der Engine).
/// Quelle und Methode stehen an jedem Kurs.
/// </summary>
public sealed class PriceService(IPriceStore store)
{
    public const string EcbSource = "EZB-Referenzkurs EUR/USD (USD-Stablecoin gleich USD)";

    /// <summary>Stablecoins, die dem US-Dollar folgen (Annahme für die Umrechnung über EZB-Kurse).</summary>
    public static readonly IReadOnlySet<string> UsdPegged = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "USDT", "USDC", "DAI", "BUSD", "TUSD", "FDUSD", "PYUSD", "USDE" };

    public async Task<EcbImportResult> ImportEcbAsync(string csv, CancellationToken cancellationToken)
    {
        var rates = EcbRates.ParseUsd(csv);
        var assets = (await store.GetAssetsAsync(cancellationToken))
            .Where(a => a.Class == AssetClass.Stablecoin && UsdPegged.Contains(a.Symbol)).ToList();

        if (assets.Count == 0)
            return new EcbImportResult(rates.Count, [], 0, "Es gibt noch keine USD-Stablecoins (z. B. USDT) in den Buchungen. Erst Buchungen importieren, dann die Kurse.");

        var prices = new List<Price>();
        foreach (var asset in assets)
            foreach (var r in rates)
                // Mitternacht Berliner Zeit des Kursdatums: jede Buchung dieses Berliner Tages findet den Tageskurs.
                prices.Add(new Price
                {
                    AssetId = asset.Id, TimestampUtc = BerlinTime.ToUtc(r.Date, TimeOnly.MinValue),
                    EurPrice = 1m / r.UsdPerEur, Source = EcbSource,
                });

        var stored = await store.UpsertAsync(prices, cancellationToken);
        return new EcbImportResult(rates.Count, assets.Select(a => a.Symbol).Order().ToList(), stored, null);
    }

    /// <summary>Einzelnen Kurs von Hand eintragen (Quelle "manuell"); Eingaben gelten als Override und werden mit Quelle gespeichert.</summary>
    public async Task<Price> SetManualAsync(string symbol, DateTimeOffset at, decimal eurPrice, string? source, CancellationToken cancellationToken)
    {
        if (eurPrice <= 0) throw new ArgumentException("Der Kurs muss größer als 0 sein (nie 0 EUR annehmen).");
        var asset = (await store.GetAssetsAsync(cancellationToken)).FirstOrDefault(a => a.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Asset '{symbol}' ist unbekannt.");
        var price = new Price { AssetId = asset.Id, TimestampUtc = at.ToUniversalTime(), EurPrice = eurPrice, Source = string.IsNullOrWhiteSpace(source) ? "manuell" : source.Trim() };
        await store.UpsertAsync([price], cancellationToken);
        return price;
    }
}
