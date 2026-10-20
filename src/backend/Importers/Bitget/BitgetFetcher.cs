using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KryptoSteuer.Importers.Bitget;

public enum BitgetAccountType { Classic, Unified }

public sealed record BitgetProbeResult(BitgetAccountType AccountType, IReadOnlyList<string> Warnings);

public sealed record BitgetFetchResult(
    string BundleJson, BitgetAccountType AccountType, IReadOnlyList<string> Warnings, IReadOnlyList<string> FailedSections,
    IReadOnlyList<string> SucceededSections);

/// <summary>
/// Ruft die Bitget-API lesend ab: Kontotyp erkennen, Rechte prüfen, Historie in 30-Tage-Fenstern (höchstens 90 Tage)
/// mit Cursor-Pagination abholen. Das Ergebnis ist ein JSON-Bündel der rohen API-Antworten
/// (siehe <see cref="BitgetBundle"/>), das der <see cref="BitgetApiImporter"/> verarbeitet.
/// </summary>
public sealed class BitgetFetcher(IBitgetClient client, TimeProvider clock)
{
    private const int PageSize = 100;

    private static readonly string[] FuturesProductTypes = ["USDT-FUTURES", "COIN-FUTURES", "USDC-FUTURES"];
    private static readonly string[] SavingsPeriods = ["flexible", "fixed"];
    private static readonly string[] ListProperties = ["list", "fillList", "bills", "dataList", "resultList", "orderList", "entrustedList"];

    // Rechte, die ein reiner Lese-Key nicht haben darf.
    private static readonly string[] DangerousPermissionHints = ["trade", "withdraw", "transfer", "write"];

    // ---- Konto erkennen und Rechte prüfen ----------------------------------------------------------------

    /// <summary>
    /// Erkennt den Kontotyp (Unified Trading Account über V3, sonst klassisch über V2) und warnt bei Handels-,
    /// Auszahlungs- oder Transferrechten. Der Key sollte nur Leserechte haben.
    /// </summary>
    public async Task<BitgetProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        try
        {
            var info = await client.GetAsync("/api/v3/account/info", [], cancellationToken);
            // Beide Aufrufe müssen gelingen: Nur ein UTA-Konto beantwortet die V3-Konto-Endpunkte.
            await client.GetAsync("/api/v3/account/settings", [], cancellationToken);

            var permissions = StringArray(info, "permissions");
            var permType = Str(info, "permType") ?? "";
            if (permissions.Any(p => DangerousPermissionHints.Any(h => p.Contains(h, StringComparison.OrdinalIgnoreCase)))
                || permType.Contains("write", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(PermissionWarning(string.Join(", ", permissions.Append(permType).Where(s => s.Length > 0))));
            }

            return new BitgetProbeResult(BitgetAccountType.Unified, warnings);
        }
        catch (BitgetApiException)
        {
            // Kein UTA-Konto (oder Key ohne Zugriff auf V3): klassisches Konto über V2 prüfen. Sind die
            // Zugangsdaten selbst ungültig, schlägt auch der V2-Aufruf unten fehl und der Fehler erreicht den Aufrufer.
        }

        var classic = await client.GetAsync("/api/v2/spot/account/info", [], cancellationToken);
        var authorities = StringArray(classic, "authorities");
        if (authorities.Any(a => DangerousPermissionHints.Any(h => a.Contains(h, StringComparison.OrdinalIgnoreCase))))
            warnings.Add(PermissionWarning(string.Join(", ", authorities)));

        return new BitgetProbeResult(BitgetAccountType.Classic, warnings);
    }

    private static string PermissionWarning(string rights) =>
        $"Der API-Key hat mehr als Leserechte ({rights}). Lege einen Key nur mit Leserechten an und trage ihn neu ein.";

    // ---- Abruf -------------------------------------------------------------------------------------------

    /// <summary>Abschnittsarten, die ein Abruf für den Kontotyp erzeugt (für den inkrementellen Sync).</summary>
    public static IReadOnlyList<string> ExpectedSections(BitgetAccountType type) => type == BitgetAccountType.Classic
        ? [BitgetBundle.SpotFills, BitgetBundle.SpotBills, BitgetBundle.Deposits, BitgetBundle.Withdrawals,
           BitgetBundle.Convert, BitgetBundle.FuturesBills, BitgetBundle.EarnSavings]
        : [BitgetBundle.UtaFills, BitgetBundle.UtaFinancial, BitgetBundle.UtaDeposits,
           BitgetBundle.UtaWithdrawals, BitgetBundle.UtaConvert];

    /// <summary>Kontotyp und Rechte werden geprüft, falls <paramref name="probe"/> nicht schon vorliegt.</summary>
    public async Task<BitgetFetchResult> FetchAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken, BitgetProbeResult? probe = null)
    {
        probe ??= await ProbeAsync(cancellationToken);
        var sections = new JsonArray();
        var failed = new List<string>();
        var symbols = new JsonArray();

        async Task Section(string kind, string endpoint, Func<Task<List<JsonElement>>> fetch)
        {
            try
            {
                var items = await fetch();
                sections.Add(new JsonObject
                {
                    ["kind"] = kind,
                    ["endpoint"] = endpoint,
                    ["items"] = new JsonArray([.. items.Select(i => JsonNode.Parse(i.GetRawText()))]),
                });
            }
            catch (BitgetApiException ex)
            {
                // Ein einzelner Endpunkt (z. B. Earn nicht aktiviert) bricht den Sync nicht ab,
                // wird aber gemeldet und der Zeitpunkt für diesen Endpunkt nicht vorgezogen.
                failed.Add($"{kind}: {ex.Message}");
            }
        }

        var windows = BitgetWindows.Split(from, to);

        if (probe.AccountType == BitgetAccountType.Classic)
        {
            await Section("symbols", "/api/v2/spot/public/symbols", async () =>
            {
                var data = await client.GetPublicAsync("/api/v2/spot/public/symbols", [], cancellationToken);
                foreach (var s in data.EnumerateArray())
                    symbols.Add(new JsonObject { ["symbol"] = Str(s, "symbol"), ["baseCoin"] = Str(s, "baseCoin"), ["quoteCoin"] = Str(s, "quoteCoin") });
                return [];
            });
            sections.Clear(); // "symbols" ist Metadaten, kein Abschnitt

            await Section(BitgetBundle.SpotFills, "/api/v2/spot/trade/fills", async () =>
            {
                var result = new List<JsonElement>();
                foreach (var (start, end) in windows)
                {
                    // Spot-Fills brauchen ein Symbol: Symbole aus der Orderhistorie des Fensters ermitteln.
                    var orders = await PageAsync("/api/v2/spot/trade/history-orders", Window(start, end), "idLessThan", "orderId", cancellationToken);
                    foreach (var symbol in orders.Select(o => Str(o, "symbol")).Where(s => s != null).Distinct())
                    {
                        var query = Window(start, end);
                        query.Insert(0, new("symbol", symbol!));
                        result.AddRange(await PageAsync("/api/v2/spot/trade/fills", query, "idLessThan", "tradeId", cancellationToken));
                    }
                }

                return result;
            });

            await Section(BitgetBundle.SpotBills, "/api/v2/spot/account/bills", () =>
                PerWindow(windows, "/api/v2/spot/account/bills", [], "idLessThan", "billId", cancellationToken));
            await Section(BitgetBundle.Deposits, "/api/v2/spot/wallet/deposit-records", () =>
                PerWindow(windows, "/api/v2/spot/wallet/deposit-records", [], "idLessThan", "orderId", cancellationToken));
            await Section(BitgetBundle.Withdrawals, "/api/v2/spot/wallet/withdrawal-records", () =>
                PerWindow(windows, "/api/v2/spot/wallet/withdrawal-records", [], "idLessThan", "orderId", cancellationToken));
            await Section(BitgetBundle.Convert, "/api/v2/convert/convert-record", () =>
                PerWindow(windows, "/api/v2/convert/convert-record", [], "idLessThan", "id", cancellationToken));

            await Section(BitgetBundle.FuturesBills, "/api/v2/mix/account/bill", async () =>
            {
                var result = new List<JsonElement>();
                foreach (var productType in FuturesProductTypes)
                    foreach (var item in await PerWindow(windows, "/api/v2/mix/account/bill",
                                 [new("productType", productType)], "idLessThan", "billId", cancellationToken))
                        result.Add(WithField(item, "productType", productType));
                return result;
            });

            await Section(BitgetBundle.EarnSavings, "/api/v2/earn/savings/records", async () =>
            {
                var result = new List<JsonElement>();
                foreach (var period in SavingsPeriods)
                    result.AddRange(await PerWindow(windows, "/api/v2/earn/savings/records",
                        [new("periodType", period)], "idLessThan", "orderId", cancellationToken));
                return result;
            });
        }
        else
        {
            await Section("symbols", "/api/v3/market/instruments", async () =>
            {
                var data = await client.GetPublicAsync("/api/v3/market/instruments", [new("category", "SPOT")], cancellationToken);
                foreach (var s in ExtractItems(data))
                    symbols.Add(new JsonObject { ["symbol"] = Str(s, "symbol"), ["baseCoin"] = Str(s, "baseCoin"), ["quoteCoin"] = Str(s, "quoteCoin") });
                return [];
            });
            sections.Clear();

            await Section(BitgetBundle.UtaFills, "/api/v3/trade/fills", () =>
                PerWindow(windows, "/api/v3/trade/fills", [new("category", "SPOT")], "cursor", null, cancellationToken));

            await Section(BitgetBundle.UtaFinancial, "/api/v3/account/financial-records", async () =>
            {
                var result = new List<JsonElement>();
                foreach (var category in new[] { "USDT-FUTURES", "COIN-FUTURES", "USDC-FUTURES", "MARGIN" })
                    foreach (var item in await PerWindow(windows, "/api/v3/account/financial-records",
                                 [new("category", category)], "cursor", null, cancellationToken))
                        result.Add(item);
                return result;
            });
            await Section(BitgetBundle.UtaDeposits, "/api/v3/account/deposit-records", () =>
                PerWindow(windows, "/api/v3/account/deposit-records", [], "cursor", null, cancellationToken));
            await Section(BitgetBundle.UtaWithdrawals, "/api/v3/account/withdrawal-records", () =>
                PerWindow(windows, "/api/v3/account/withdrawal-records", [], "cursor", null, cancellationToken));
            await Section(BitgetBundle.UtaConvert, "/api/v3/account/convert-records", () =>
                PerWindow(windows, "/api/v3/account/convert-records", [], "cursor", null, cancellationToken));
        }

        var bundle = new JsonObject
        {
            ["format"] = BitgetBundle.Format,
            ["accountType"] = probe.AccountType == BitgetAccountType.Unified ? BitgetBundle.UnifiedAccount : BitgetBundle.ClassicAccount,
            ["fetchedAtUtc"] = clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
            ["from"] = from.ToString("O", CultureInfo.InvariantCulture),
            ["to"] = to.ToString("O", CultureInfo.InvariantCulture),
            ["symbols"] = symbols,
            ["sections"] = sections,
        };

        var succeeded = sections.Select(x => x!["kind"]!.GetValue<string>()).ToList();
        return new BitgetFetchResult(bundle.ToJsonString(), probe.AccountType, probe.Warnings, failed, succeeded);
    }

    // ---- Hilfsfunktionen ---------------------------------------------------------------------------------

    private static List<KeyValuePair<string, string>> Window(DateTimeOffset start, DateTimeOffset end) =>
    [
        new("startTime", start.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)),
        new("endTime", end.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)),
    ];

    private async Task<List<JsonElement>> PerWindow(
        IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> windows, string path,
        List<KeyValuePair<string, string>> extra, string cursorParam, string? idProperty, CancellationToken cancellationToken)
    {
        var result = new List<JsonElement>();
        foreach (var (start, end) in windows)
        {
            var query = new List<KeyValuePair<string, string>>(extra);
            query.AddRange(Window(start, end));
            result.AddRange(await PageAsync(path, query, cursorParam, idProperty, cancellationToken));
        }

        return result;
    }

    /// <summary>
    /// Holt alle Seiten eines Endpunkts. Cursor: das Feld <c>endId</c>/<c>cursor</c> der Antwort, bei reinen
    /// Arrays die ID des letzten Elements (<paramref name="idProperty"/>). Ohne ID wird das Fenster halbiert,
    /// sobald eine Seite voll ist.
    /// </summary>
    private async Task<List<JsonElement>> PageAsync(
        string path, List<KeyValuePair<string, string>> baseQuery, string cursorParam, string? idProperty,
        CancellationToken cancellationToken)
    {
        var all = new List<JsonElement>();
        string? cursor = null;

        while (true)
        {
            var query = new List<KeyValuePair<string, string>>(baseQuery) { new("limit", PageSize.ToString(CultureInfo.InvariantCulture)) };
            if (cursor != null) query.Add(new(cursorParam, cursor));

            var data = await client.GetAsync(path, query, cancellationToken);
            var items = ExtractItems(data);
            all.AddRange(items);

            string? next = null;
            if (data.ValueKind == JsonValueKind.Object)
            {
                next = Str(data, "endId") ?? Str(data, "cursor");
                if (string.IsNullOrEmpty(next)) next = null;
            }
            else if (items.Count >= PageSize && idProperty != null)
            {
                next = Str(items[^1], idProperty);
            }
            else if (items.Count >= PageSize && idProperty == null)
            {
                return await BisectAsync(path, baseQuery, cursorParam, cancellationToken);
            }

            if (items.Count < PageSize || next == null || next == cursor)
                return all;
            cursor = next;
        }
    }

    /// <summary>Notlösung für Endpunkte ohne nutzbaren Cursor: Zeitfenster halbieren, bis jede Seite unter dem Limit bleibt.</summary>
    private async Task<List<JsonElement>> BisectAsync(
        string path, List<KeyValuePair<string, string>> baseQuery, string cursorParam, CancellationToken cancellationToken)
    {
        var start = long.Parse(baseQuery.First(q => q.Key == "startTime").Value, CultureInfo.InvariantCulture);
        var end = long.Parse(baseQuery.First(q => q.Key == "endTime").Value, CultureInfo.InvariantCulture);
        var all = new List<JsonElement>();
        var stack = new Stack<(long, long)>();
        stack.Push((start, end));

        while (stack.Count > 0)
        {
            var (s, e) = stack.Pop();
            var query = baseQuery.Where(q => q.Key is not ("startTime" or "endTime" or "limit")).ToList();
            query.Add(new("startTime", s.ToString(CultureInfo.InvariantCulture)));
            query.Add(new("endTime", e.ToString(CultureInfo.InvariantCulture)));
            query.Add(new("limit", PageSize.ToString(CultureInfo.InvariantCulture)));

            var items = ExtractItems(await client.GetAsync(path, query, cancellationToken));
            if (items.Count >= PageSize && e - s > 1)
            {
                var mid = s + (e - s) / 2;
                stack.Push((mid, e));
                stack.Push((s, mid));
            }
            else
            {
                all.AddRange(items);
            }
        }

        return all;
    }

    private static List<JsonElement> ExtractItems(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
            return [.. data.EnumerateArray()];
        if (data.ValueKind == JsonValueKind.Object)
            foreach (var name in ListProperties)
                if (data.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array)
                    return [.. list.EnumerateArray()];
        return [];
    }

    private static JsonElement WithField(JsonElement item, string name, string value)
    {
        var node = JsonNode.Parse(item.GetRawText())!.AsObject();
        node[name] = value;
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    private static List<string> StringArray(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Select(e => e.GetString() ?? "")]
            : [];
}
