using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Importers.Bitget;

/// <summary>
/// Verarbeitet das JSON-Bündel der Bitget-API-Antworten (<see cref="BitgetBundle"/>). Jede Zeile eines API-Antwort-Arrays
/// wird zu einem <see cref="RawRecord"/> mit dem Originaljson; <c>ExternalId</c> ist <c>&lt;Art&gt;:&lt;ID&gt;</c>.
/// Annahmen und Zuordnung der Buchungsarten stehen in docs/04-importe.md.
/// </summary>
public sealed class BitgetApiImporter : IImporter
{
    public const string SourceName = "bitget-api";

    // Spot-Bills, die schon über Fills, Ein-/Auszahlungen oder Gebühren in den Fills abgedeckt sind
    // oder nur Sperrzustände sind. Sie werden nicht gelesen, damit nichts doppelt gebucht wird.
    private static readonly HashSet<string> CoveredSpotBillTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "deposit", "withdraw", "buy", "sell", "deduction of handling fee",
        "transfer-in", "transfer-out", "system lock", "user lock",
    };

    public string Source => SourceName;

    public bool CanHandle(ImportInput input)
    {
        try
        {
            using var doc = JsonDocument.Parse(input.Content);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("format", out var f)
                && f.GetString() == BitgetBundle.Format;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ---- Parse -------------------------------------------------------------------------------------------

    public IEnumerable<RawRecord> Parse(ImportInput input, ImportContext context)
    {
        using var doc = JsonDocument.Parse(input.Content);
        var root = doc.RootElement;

        var symbols = new Dictionary<string, (string Base, string Quote)>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("symbols", out var symbolList) && symbolList.ValueKind == JsonValueKind.Array)
            foreach (var s in symbolList.EnumerateArray())
                if (Str(s, "symbol") is { } sym && Str(s, "baseCoin") is { } b && Str(s, "quoteCoin") is { } q)
                    symbols[sym] = (b, q);

        var accountType = Str(root, "accountType") ?? BitgetBundle.ClassicAccount;
        var index = 0;

        foreach (var section in root.GetProperty("sections").EnumerateArray())
        {
            var kind = Str(section, "kind") ?? "";
            foreach (var item in section.GetProperty("items").EnumerateArray())
            {
                index++;
                if (IsCovered(kind, item))
                    continue;

                context.RowsRead++;
                var id = ExternalIdOf(kind, item);
                if (id is null)
                {
                    context.AddError(index, $"{kind}: Eintrag ohne eindeutige ID.");
                    continue;
                }

                var record = new JsonObject
                {
                    ["kind"] = kind,
                    ["accountType"] = accountType,
                    ["item"] = JsonNode.Parse(item.GetRawText()),
                };
                var symbol = Str(item, "symbol");
                if (symbol != null && symbols.TryGetValue(symbol, out var pair))
                {
                    record["baseCoin"] = pair.Base;
                    record["quoteCoin"] = pair.Quote;
                }

                var payload = record.ToJsonString();
                yield return new RawRecord
                {
                    Source = Source,
                    ExternalId = $"{kind}:{id}",
                    Payload = payload,
                    FileHash = context.FileHash,
                    ContentHash = IdempotencyKey.Sha256Hex(payload),
                    LineNumber = index,
                    ImportRunId = context.ImportRunId,
                    ImportedAtUtc = context.ImportedAtUtc,
                };
            }
        }
    }

    private static bool IsCovered(string kind, JsonElement item) => kind switch
    {
        BitgetBundle.SpotBills => Str(item, "businessType") is { } t && CoveredSpotBillTypes.Contains(t),
        // Nur abgeschlossene Ein-/Auszahlungen; ausstehende werden beim nächsten Sync (Überlappung) erneut abgefragt.
        BitgetBundle.Deposits or BitgetBundle.Withdrawals or BitgetBundle.UtaDeposits or BitgetBundle.UtaWithdrawals
            => !string.Equals(Str(item, "status"), "success", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static string? ExternalIdOf(string kind, JsonElement item) => kind switch
    {
        BitgetBundle.SpotFills => Str(item, "tradeId"),
        BitgetBundle.SpotBills => Str(item, "billId"),
        BitgetBundle.Deposits or BitgetBundle.Withdrawals => Str(item, "orderId") ?? Str(item, "tradeId"),
        BitgetBundle.FuturesBills => Str(item, "billId"),
        BitgetBundle.Convert => Str(item, "id"),
        BitgetBundle.EarnSavings => Str(item, "orderId") is { } o ? $"{o}:{Str(item, "ts")}:{Str(item, "orderType")}" : null,
        BitgetBundle.UtaFills => Str(item, "execId"),
        BitgetBundle.UtaFinancial => Str(item, "id"),
        BitgetBundle.UtaDeposits or BitgetBundle.UtaWithdrawals => Str(item, "recordId") ?? Str(item, "orderId"),
        BitgetBundle.UtaConvert => Str(item, "ts") is { } ts
            ? $"{ts}:{Str(item, "fromCoin")}:{Str(item, "fromCoinSize")}:{Str(item, "toCoin")}:{Str(item, "toCoinSize")}"
            : null,
        _ => null,
    };

    // ---- Normalize ---------------------------------------------------------------------------------------

    public IReadOnlyList<Transaction> Normalize(RawRecord record, ImportContext context)
    {
        using var doc = JsonDocument.Parse(record.Payload);
        var root = doc.RootElement;
        var kind = Str(root, "kind") ?? "";
        var item = root.GetProperty("item");
        var baseCoin = Str(root, "baseCoin");
        var quoteCoin = Str(root, "quoteCoin");
        var b = new BitgetTxBuilder(record, context);

        switch (kind)
        {
            case BitgetBundle.SpotFills:
                return b.One(Trade(b, item, baseCoin, quoteCoin, Str(item, "side"),
                    Dec(item, "size"), Dec(item, "amount"), Time(item, "cTime"),
                    Fees(item.TryGetProperty("feeDetail", out var fd) ? fd : default)));

            case BitgetBundle.UtaFills:
                return b.One(Trade(b, item, baseCoin, quoteCoin, Str(item, "side"),
                    Dec(item, "execQty"), Dec(item, "execValue"), Time(item, "createdTime"),
                    Fees(item.TryGetProperty("feeDetail", out var fd3) ? fd3 : default)));

            case BitgetBundle.SpotBills:
                return b.One(SpotBill(b, item));

            case BitgetBundle.Deposits:
            case BitgetBundle.UtaDeposits:
                return b.One(b.Build(TransactionType.Deposit, Time(item, kind == BitgetBundle.Deposits ? "cTime" : "createdTime"),
                    [b.Leg(Req(item, "coin"), Positive(Dec(item, "size"), "size"), LegDirection.In)], note: DestNote(item)));

            case BitgetBundle.Withdrawals:
            case BitgetBundle.UtaWithdrawals:
            {
                var coin = Req(item, "coin");
                var legs = new List<TransactionLeg> { b.Leg(coin, Positive(Dec(item, "size"), "size"), LegDirection.Out) };
                var fee = Math.Abs(Dec(item, "fee") ?? 0m);
                if (fee > 0) legs.Add(b.Leg(coin, fee, LegDirection.Out, isFee: true));
                return b.One(b.Build(TransactionType.Withdrawal,
                    Time(item, kind == BitgetBundle.Withdrawals ? "cTime" : "createdTime"), legs, note: DestNote(item)));
            }

            case BitgetBundle.FuturesBills:
                return b.One(BitgetFuturesMapper.Map(b, Str(item, "businessType") ?? "", Req(item, "coin"),
                    Dec(item, "amount") ?? 0m, Dec(item, "fee") ?? 0m, Time(item, "cTime"), Str(item, "symbol")));

            case BitgetBundle.UtaFinancial:
                return b.One(BitgetFuturesMapper.Map(b, Str(item, "type") ?? "", Req(item, "coin"),
                    Dec(item, "amount") ?? 0m, Dec(item, "fee") ?? 0m, Time(item, "ts"), Str(item, "symbol")));

            case BitgetBundle.Convert:
                return b.One(ConvertTx(b, item));

            case BitgetBundle.UtaConvert:
                return b.One(ConvertTx(b, item));

            case BitgetBundle.EarnSavings:
                return b.One(EarnSavings(b, item));

            default:
                throw new ImportRowException($"Unbekannte Abschnittsart '{kind}'.");
        }
    }

    // Spot-/UTA-Fill: buy = Base rein, Quote raus; sell = Base raus, Quote rein.
    private static Transaction Trade(
        BitgetTxBuilder b, JsonElement item, string? baseCoin, string? quoteCoin, string? side,
        decimal? baseAmount, decimal? quoteAmount, DateTimeOffset time, IReadOnlyList<(string Coin, decimal Amount)> fees)
    {
        if (baseCoin is null || quoteCoin is null)
            throw new ImportRowException($"Symbol '{Str(item, "symbol")}' ist unbekannt (Basis-/Quote-Coin fehlen).");

        var buy = string.Equals(side, "buy", StringComparison.OrdinalIgnoreCase);
        if (!buy && !string.Equals(side, "sell", StringComparison.OrdinalIgnoreCase))
            throw new ImportRowException($"Unbekannte Seite '{side}'.");

        var baseQty = Positive(baseAmount, "size");
        var quoteQty = Positive(quoteAmount, "amount");
        var legs = new List<TransactionLeg>
        {
            b.Leg(baseCoin, baseQty, buy ? LegDirection.In : LegDirection.Out),
            b.Leg(quoteCoin, quoteQty, buy ? LegDirection.Out : LegDirection.In),
        };

        // Bitget meldet mehrere Gebührenanteile; höchstens ein Gebührenbein je Buchung: gleiche Coins zusammenfassen.
        var grouped = fees.Where(f => f.Amount != 0).GroupBy(f => f.Coin).ToList();
        if (grouped.Count > 1)
            throw new ImportRowException("Gebühren in mehreren Coins in einer Ausführung werden nicht unterstützt.");
        if (grouped.Count == 1)
            legs.Add(b.Leg(grouped[0].Key, grouped[0].Sum(f => Math.Abs(f.Amount)), LegDirection.Out, isFee: true));

        return b.Build(buy ? TransactionType.Buy : TransactionType.Sell, time, legs);
    }

    private static Transaction SpotBill(BitgetTxBuilder b, JsonElement item)
    {
        var business = Str(item, "businessType") ?? "";
        var coin = Req(item, "coin");
        var size = Dec(item, "size") ?? 0m;
        var time = Time(item, "cTime");

        TransactionType? type = business.ToLowerInvariant() switch
        {
            "rebate rewards" or "usdt contract rewards" or "mix contract rewards" => TransactionType.Bonus,
            "airdrop rewards" => TransactionType.Airdrop,
            _ => null,
        };

        if (type is null || size <= 0)
            return b.Review(time, business.Length > 0 ? business : "spot-bill",
                size == 0 ? [] : [b.Leg(coin, Math.Abs(size), size < 0 ? LegDirection.Out : LegDirection.In)]);

        return b.Build(type.Value, time, [b.Leg(coin, size, LegDirection.In)], note: business);
    }

    private static Transaction ConvertTx(BitgetTxBuilder b, JsonElement item)
    {
        var time = Time(item, "ts");
        var fee = Dec(item, "fee");
        return b.Build(TransactionType.Trade, time,
        [
            b.Leg(Req(item, "toCoin"), Positive(Dec(item, "toCoinSize"), "toCoinSize"), LegDirection.In),
            b.Leg(Req(item, "fromCoin"), Positive(Dec(item, "fromCoinSize"), "fromCoinSize"), LegDirection.Out),
        ], note: fee is > 0 ? $"Convert (Gebühr laut Bitget: {fee.Value.ToString(CultureInfo.InvariantCulture)}, Coin unbekannt, nicht gebucht)" : "Convert");
    }

    private static Transaction EarnSavings(BitgetTxBuilder b, JsonElement item)
    {
        var orderType = Str(item, "orderType") ?? "";
        var time = Time(item, "ts");
        var amount = Dec(item, "amount") ?? 0m;
        var o = orderType.ToLowerInvariant();

        if (o.Contains("interest") || o.Contains("profit") || o.Contains("reward") || o.Contains("earn"))
            return amount > 0
                ? b.Build(TransactionType.Interest, time,
                    [b.Leg(Str(item, "settleCoinName") ?? Req(item, "coinName"), amount, LegDirection.In)], note: $"Earn: {orderType}")
                : b.Review(time, orderType, []);

        if (o.Contains("subscribe") || o.Contains("redeem"))
            return b.Ignored(time, orderType, "Umbuchung zwischen Spot- und Earn-Konto");

        return b.Review(time, orderType.Length > 0 ? orderType : "earn-savings",
            amount > 0 ? [b.Leg(Req(item, "coinName"), amount, LegDirection.In)] : []);
    }

    // ---- Hilfen ------------------------------------------------------------------------------------------

    private static string DestNote(JsonElement item) =>
        string.Equals(Str(item, "dest"), "internal_transfer", StringComparison.OrdinalIgnoreCase)
            ? "interner Transfer (Bitget-Nutzer)"
            : Str(item, "chain") is { Length: > 0 } chain ? $"on-chain: {chain}" : "";

    private static IReadOnlyList<(string Coin, decimal Amount)> Fees(JsonElement feeDetail)
    {
        var result = new List<(string, decimal)>();
        IEnumerable<JsonElement> entries = feeDetail.ValueKind switch
        {
            JsonValueKind.Array => feeDetail.EnumerateArray(),
            JsonValueKind.Object => [feeDetail],
            _ => [],
        };

        foreach (var e in entries)
        {
            var coin = Str(e, "feeCoin") ?? Str(e, "feeCoinCode");
            var amount = Dec(e, "totalFee") ?? Dec(e, "fee");
            if (coin != null && amount is { } a && a != 0)
                result.Add((coin, a));
        }

        return result;
    }

    private static string Req(JsonElement e, string name) =>
        Str(e, name) is { Length: > 0 } v ? v : throw new ImportRowException($"Feld '{name}' fehlt.");

    private static decimal Positive(decimal? value, string name) =>
        value is > 0 ? value.Value : throw new ImportRowException($"Feld '{name}' muss größer als 0 sein.");

    private static DateTimeOffset Time(JsonElement e, string name)
    {
        var text = Str(e, name);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : throw new ImportRowException($"Ungültiger Zeitstempel in '{name}': '{text}'.");
    }

    private static decimal? Dec(JsonElement e, string name)
    {
        var text = Str(e, name);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new ImportRowException($"Ungültige Zahl in '{name}': '{text}'.");
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Null => null, _ => v.ToString() }
            : null;
}
