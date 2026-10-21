using System.Globalization;
using System.Text.Json;
using KryptoSteuer.Application.Bitget;

namespace KryptoSteuer.Importers.Bitget;

/// <summary>
/// Ruft die aktuellen Kontostände aller Wallets ab (Spot, Futures, Earn, Funding bzw. Unified + Funding) und summiert sie je Coin.
/// Fällt ein Endpunkt aus, wird kein Teilergebnis geliefert, damit fehlende Wallets keine falschen Abweichungen erzeugen.
/// </summary>
public static class BitgetBalances
{
    private static readonly string[] FuturesProductTypes = ["USDT-FUTURES", "COIN-FUTURES", "USDC-FUTURES"];

    public static async Task<IReadOnlyDictionary<string, decimal>> FetchTotalsAsync(
        IBitgetClient client, BitgetAccountType type, CancellationToken cancellationToken)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        void Add(JsonElement item, string coinProp, params string[] amountProps)
        {
            var coin = Str(item, coinProp);
            if (string.IsNullOrEmpty(coin)) return;
            var sum = amountProps.Sum(p => Dec(item, p));
            totals[coin] = totals.GetValueOrDefault(coin) + sum;
        }

        if (type == BitgetAccountType.Classic)
        {
            foreach (var a in (await client.GetAsync("/api/v2/spot/account/assets", [], cancellationToken)).EnumerateArray())
                Add(a, "coin", "available", "frozen", "locked");

            foreach (var productType in FuturesProductTypes)
            {
                var data = await client.GetAsync("/api/v2/mix/account/accounts", [new("productType", productType)], cancellationToken);
                foreach (var a in data.EnumerateArray())
                    Add(a, "marginCoin", "available", "locked");
            }

            foreach (var a in (await client.GetAsync("/api/v2/earn/account/assets", [], cancellationToken)).EnumerateArray())
                Add(a, "coin", "amount");

            foreach (var a in (await client.GetAsync("/api/v2/account/funding-assets", [], cancellationToken)).EnumerateArray())
                Add(a, "coin", "available", "frozen");
        }
        else
        {
            var unified = await client.GetAsync("/api/v3/account/assets", [], cancellationToken);
            if (unified.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                    Add(a, "coin", "balance");

            foreach (var a in (await client.GetAsync("/api/v3/account/funding-assets", [], cancellationToken)).EnumerateArray())
                Add(a, "coin", "balance");
        }

        return totals;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;

    private static decimal Dec(JsonElement e, string name) =>
        decimal.TryParse(Str(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}

/// <summary>Vergleicht Börsen-Salden mit dem aus den Buchungen berechneten Bestand.</summary>
public static class BalanceReconciler
{
    public const decimal AbsoluteTolerance = 0.00000001m;
    public const decimal RelativeTolerance = 0.000001m;

    public static IReadOnlyList<BalanceMismatch> Compare(
        IReadOnlyDictionary<string, decimal> exchange, IReadOnlyDictionary<string, decimal> computed)
    {
        var symbols = exchange.Keys.Concat(computed.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        var result = new List<BalanceMismatch>();

        foreach (var symbol in symbols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            var onExchange = Lookup(exchange, symbol);
            var holding = Lookup(computed, symbol);
            var tolerance = Math.Max(AbsoluteTolerance, RelativeTolerance * Math.Abs(onExchange));
            if (Math.Abs(onExchange - holding) > tolerance)
                result.Add(new BalanceMismatch(symbol, onExchange, holding));
        }

        return result;
    }

    private static decimal Lookup(IReadOnlyDictionary<string, decimal> map, string symbol) =>
        map.FirstOrDefault(kv => string.Equals(kv.Key, symbol, StringComparison.OrdinalIgnoreCase)).Value;
}
