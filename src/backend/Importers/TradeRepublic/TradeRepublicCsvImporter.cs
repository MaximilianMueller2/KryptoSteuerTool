using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Importers.Common;

namespace KryptoSteuer.Importers.TradeRepublic;

/// <summary>
/// Trade-Republic-Transaktionsexport (App: Kontoauszüge → Transaktionsexport). Spalten (Auszug):
/// <c>datetime, date, account_type, category, type, asset_class, name, symbol, shares, price, amount, fee, tax, currency, …, transaction_id</c>.
/// Besonderheiten: alle Felder in Anführungszeichen, <c>datetime</c> in UTC (ISO 8601), Zeilen nicht streng nach Zeit sortiert,
/// <c>amount</c> ist brutto (Stückzahl mal Preis), Gebühr und Steuer stehen in eigenen Spalten.
/// Krypto bildet die Grundlage für die Krypto-Steuerberechnung; Aktien, Fonds und Derivate werden als Buchungen gespeichert
/// und später im Aktien/ETF-Modul verarbeitet. Zuordnung und Annahmen: docs/04-importe.md.
/// </summary>
public sealed partial class TradeRepublicCsvImporter : IImporter
{
    public const string SourceName = "traderepublic-csv";

    private const string Eur = "EUR";

    private static readonly string[] RequiredColumns =
        ["datetime", "category", "type", "asset_class", "name", "symbol", "shares", "price", "amount", "fee", "tax", "currency", "transaction_id"];

    private static readonly HashSet<string> CashMovementTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CUSTOMER_INBOUND", "CUSTOMER_INPAYMENT", "CUSTOMER_OUTBOUND_REQUEST",
        "TRANSFER_INBOUND", "TRANSFER_OUTBOUND", "TRANSFER_INSTANT_INBOUND", "TRANSFER_INSTANT_OUTBOUND",
        "TRANSFER_DIRECT_DEBIT_INBOUND",
    };

    private static readonly HashSet<string> CardTypes = new(StringComparer.OrdinalIgnoreCase)
        { "CARD_TRANSACTION", "CARD_TRANSACTION_INTERNATIONAL" };

    public string Source => SourceName;

    public bool CanHandle(ImportInput input)
    {
        var header = CsvReader.Read(input.Content).FirstOrDefault();
        return header is not null && RequiredColumns.All(c => IndexOf(header, c) >= 0);
    }

    // ---- Parse -------------------------------------------------------------------------------------------

    public IEnumerable<RawRecord> Parse(ImportInput input, ImportContext context)
    {
        CsvRow? header = null;
        var cardPayments = 0;

        foreach (var row in CsvReader.Read(input.Content))
        {
            if (header is null)
            {
                header = row;
                var missing = RequiredColumns.Where(c => IndexOf(row, c) < 0).ToList();
                if (missing.Count > 0)
                {
                    context.AddError(row.LineNumber, $"Spalten fehlen: {string.Join(", ", missing)}.");
                    yield break;
                }

                continue;
            }

            context.RowsRead++;
            if (row.Fields.Count != header.Fields.Count)
            {
                context.AddError(row.LineNumber, $"{row.Fields.Count} Spalten statt {header.Fields.Count}.");
                continue;
            }

            var id = Field(header, row, "transaction_id").Trim();
            if (id.Length == 0)
            {
                context.AddError(row.LineNumber, "transaction_id fehlt.");
                continue;
            }

            if (CardTypes.Contains(Field(header, row, "type").Trim())) cardPayments++;

            var payload = header.RawText + "\n" + row.RawText;
            yield return new RawRecord
            {
                Source = Source,
                ExternalId = $"tr:{id}",
                Payload = payload,
                FileHash = context.FileHash,
                ContentHash = IdempotencyKey.Sha256Hex(payload),
                LineNumber = row.LineNumber,
                ImportRunId = context.ImportRunId,
                ImportedAtUtc = context.ImportedAtUtc,
            };
        }

        if (cardPayments > 0)
            context.AddWarning(
                $"{cardPayments} Kartenzahlungen werden als steuerlich nicht relevant mit Status Ignored gespeichert (Rohdaten bleiben erhalten).");
    }

    // ---- Normalize ---------------------------------------------------------------------------------------

    public IReadOnlyList<Transaction> Normalize(RawRecord record, ImportContext context)
    {
        var rows = CsvReader.Read(Encoding.UTF8.GetBytes(record.Payload)).ToList();
        var header = rows[0];
        var row = rows[1];
        string F(string column) => Field(header, row, column).Trim();

        var category = F("category").ToUpperInvariant();
        var type = F("type").ToUpperInvariant();
        var assetClass = F("asset_class").ToUpperInvariant();
        var label = $"{category}/{type}";

        var time = DateTimeOffset.TryParse(F("datetime"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : throw new ImportRowException($"Ungültiger Zeitstempel '{F("datetime")}'.");

        var currency = F("currency");
        if (currency.Length > 0 && !currency.Equals(Eur, StringComparison.OrdinalIgnoreCase))
            throw new ImportRowException($"Währung '{currency}' wird nicht unterstützt (nur EUR).");

        var shares = Dec(F("shares"), "shares");
        var amount = Dec(F("amount"), "amount");
        var fee = Dec(F("fee"), "fee") ?? 0m;
        var tax = Dec(F("tax"), "tax") ?? 0m;
        var name = F("name");
        var symbol = F("symbol");

        var b = new ImportTxBuilder(record, context);
        var result = new List<Transaction>();

        // Steuer, die TR einbehalten (negativ) oder erstattet (positiv) hat, als eigene Buchung.
        void AddTax(string note)
        {
            if (tax == 0) return;
            result.Add(b.Build(TransactionType.Tax, time,
                [b.Leg(Eur, AssetClass.Fiat, null, null, Math.Abs(tax), tax < 0 ? LegDirection.Out : LegDirection.In)],
                note: tax < 0 ? $"{note}: Steuer einbehalten" : $"{note}: Steuer erstattet", externalIdSuffix: ":tax"));
        }

        TransactionLeg EurLeg(decimal value, LegDirection direction, bool isFee = false) =>
            b.Leg(Eur, AssetClass.Fiat, null, null, value, direction, isFee);

        string Describe(string what) => name.Length > 0 ? $"{what} {name}" : what;

        switch (category)
        {
            case "TRADING" when type is "BUY" or "SELL":
            {
                var cls = ParseClass(assetClass);
                var (assetSymbol, assetName, isin) = AssetIdentity(cls, symbol, name);
                var qty = Math.Abs(shares ?? throw new ImportRowException("Spalte shares fehlt."));
                var cash = Math.Abs(amount ?? throw new ImportRowException("Spalte amount fehlt."));
                if (qty == 0 || cash == 0) throw new ImportRowException("Stückzahl und Betrag müssen größer als 0 sein.");
                var buy = type == "BUY";
                if (buy && shares < 0 || !buy && shares > 0)
                    throw new ImportRowException($"Vorzeichen der Stückzahl passt nicht zu {type}.");

                var legs = new List<TransactionLeg>
                {
                    b.Leg(assetSymbol, cls, assetName, isin, qty, buy ? LegDirection.In : LegDirection.Out),
                    EurLeg(cash, buy ? LegDirection.Out : LegDirection.In),
                };
                if (fee != 0) legs.Add(EurLeg(Math.Abs(fee), LegDirection.Out, isFee: true));
                result.Add(b.Build(buy ? TransactionType.Buy : TransactionType.Sell, time, legs, note: Describe(buy ? "Kauf" : "Verkauf")));
                AddTax(Describe(buy ? "Kauf" : "Verkauf"));
                return result;
            }

            case "DELIVERY" when type == "FREE_DELIVERY" && assetClass == "CRYPTO" && shares is not null and not 0:
            {
                // Krypto wird an eine externe Wallet bzw. Börse geliefert: Übertragung, kein Verkauf (Zuordnung in P10).
                var (assetSymbol, assetName, isin) = AssetIdentity(AssetClass.Crypto, symbol, name);
                var qty = Math.Abs(shares.Value);
                result.Add(b.Build(shares < 0 ? TransactionType.TransferOut : TransactionType.TransferIn, time,
                    [b.Leg(assetSymbol, AssetClass.Crypto, assetName, isin, qty, shares < 0 ? LegDirection.Out : LegDirection.In)],
                    note: Describe("Lieferung von Krypto")));
                return result;
            }

            case "CORPORATE_ACTION":
            {
                // Kapitalmaßnahmen (Spin-off, Optionsschein-Ausübung, ...) werden erst im Aktien/ETF-Modul abgebildet.
                var legs = new List<TransactionLeg>();
                if (shares is not null and not 0 && symbol.Length > 0)
                {
                    var cls = ParseClass(assetClass);
                    var (assetSymbol, assetName, isin) = AssetIdentity(cls, symbol, name);
                    legs.Add(b.Leg(assetSymbol, cls, assetName, isin, Math.Abs(shares.Value), shares < 0 ? LegDirection.Out : LegDirection.In));
                }

                result.Add(b.Review(time, label, legs, $"Kapitalmaßnahme {type}: {name}".TrimEnd(' ', ':')));
                return result;
            }

            case "CASH":
                break;

            default:
                result.Add(b.Review(time, label, [], $"Unbekannte Buchungsart {label}"));
                return result;
        }

        // ---- CASH ----
        switch (type)
        {
            case "INTEREST_PAYMENT":
                if (amount is > 0)
                    result.Add(b.Build(TransactionType.Interest, time, [EurLeg(amount.Value, LegDirection.In)], note: "Zinsen auf Guthaben"));
                AddTax("Zinsen auf Guthaben");
                return NonEmpty(result, b, time, label);

            case "DIVIDEND":
                if (amount is > 0)
                    result.Add(b.Build(TransactionType.Dividend, time, [EurLeg(amount.Value, LegDirection.In)], note: Describe("Dividende")));
                AddTax(Describe("Dividende"));
                return NonEmpty(result, b, time, label);

            case "EARNINGS":
                // Fondserträge: Ausschüttung (Betrag > 0) oder nur Steuer auf die Vorabpauschale (Betrag 0).
                if (amount is > 0)
                    result.Add(b.Build(TransactionType.Dividend, time, [EurLeg(amount.Value, LegDirection.In)], note: Describe("Fondsertrag")));
                AddTax(Describe(amount is > 0 ? "Fondsertrag" : "Vorabpauschale"));
                return NonEmpty(result, b, time, label);

            case "TAX_OPTIMIZATION":
                AddTax("Steueroptimierung (Verlustverrechnung)");
                return NonEmpty(result, b, time, label);

            case "STOCKPERK" or "BENEFITS_SAVEBACK":
                if (amount is not > 0) throw new ImportRowException("Betrag muss größer als 0 sein.");
                result.Add(b.Build(TransactionType.Bonus, time, [EurLeg(amount.Value, LegDirection.In)],
                    note: Describe(type == "STOCKPERK" ? "Stockperk" : "Saveback")));
                return result;

            case "GIFT":
                if (amount is null or 0) throw new ImportRowException("Betrag fehlt.");
                result.Add(b.Build(amount > 0 ? TransactionType.GiftIn : TransactionType.GiftOut, time,
                    [EurLeg(Math.Abs(amount.Value), amount > 0 ? LegDirection.In : LegDirection.Out)], note: "Geschenk (Gutschrift bzw. Weitergabe)"));
                return result;

            case "FEE" or "CARD_ORDERING_FEE":
                if (fee == 0) return [b.Ignored(time, label, "Gebühr 0")];
                result.Add(b.Build(TransactionType.Fee, time, [EurLeg(Math.Abs(fee), LegDirection.Out)],
                    note: type == "CARD_ORDERING_FEE" ? "Kartengebühr" : "Gebühr"));
                return result;

            case "TILG":
                // Tilgung eines Optionsscheins nach Ausübung; gehört zur Kapitalmaßnahme, daher zur Prüfung.
                return [b.Review(time, label, amount is > 0 ? [EurLeg(amount.Value, LegDirection.In)] : [],
                    Describe("Tilgung/Ausübung Optionsschein"))];
        }

        if (CardTypes.Contains(type))
            return [b.Ignored(time, label, "Kartenzahlung, steuerlich nicht relevant")];

        if (CashMovementTypes.Contains(type))
        {
            if (amount is null or 0) return [b.Ignored(time, label, "Betrag 0")];
            var inbound = amount > 0;
            return [b.Build(inbound ? TransactionType.Deposit : TransactionType.Withdrawal, time,
                [EurLeg(Math.Abs(amount.Value), inbound ? LegDirection.In : LegDirection.Out)],
                note: inbound ? "Einzahlung auf das Verrechnungskonto" : "Auszahlung vom Verrechnungskonto")];
        }

        return [b.Review(time, label, amount is not null and not 0
            ? [EurLeg(Math.Abs(amount.Value), amount > 0 ? LegDirection.In : LegDirection.Out)] : [],
            $"Unbekannte Buchungsart {label}")];
    }

    /// <summary>Zeilen, die weder Betrag noch Steuer enthalten, werden nicht verworfen, sondern als Ignored gespeichert.</summary>
    private static List<Transaction> NonEmpty(List<Transaction> result, ImportTxBuilder b, DateTimeOffset time, string label) =>
        result.Count > 0 ? result : [b.Ignored(time, label, "Betrag und Steuer 0")];

    // ---- Hilfen ------------------------------------------------------------------------------------------

    private static AssetClass ParseClass(string assetClass) => assetClass switch
    {
        "CRYPTO" => AssetClass.Crypto,
        "STOCK" => AssetClass.Stock,
        "FUND" => AssetClass.Etf,
        "DERIVATIVE" => AssetClass.Derivative,
        _ => throw new ImportRowException($"Unbekannte asset_class '{assetClass}'."),
    };

    /// <summary>
    /// Krypto: Ticker (BTC, SOL, ...). TR nennt manche Coins nur mit Pseudo-ISIN (z. B. <c>XF0RENDER015</c>); dann gilt der Name
    /// als Ticker. Wertpapiere: die ISIN ist das Symbol.
    /// </summary>
    private static (string Symbol, string? Name, string? Isin) AssetIdentity(AssetClass cls, string symbol, string name)
    {
        if (symbol.Length == 0) throw new ImportRowException("Spalte symbol fehlt.");
        if (cls == AssetClass.Crypto)
        {
            var ticker = PseudoIsin().IsMatch(symbol) && name.Length > 0 && !name.Contains(' ') ? name.ToUpperInvariant() : symbol;
            return (ticker, name.Length > 0 ? name : null, null);
        }

        return (symbol, name.Length > 0 ? name : null, symbol);
    }

    [GeneratedRegex("^XF[0-9A-Z]{10}$")]
    private static partial Regex PseudoIsin();

    private static decimal? Dec(string text, string column)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new ImportRowException($"Ungültige Zahl in '{column}': '{text}'.");
    }

    private static int IndexOf(CsvRow header, string column) =>
        header.Fields.ToList().FindIndex(h => string.Equals(h.Trim(), column, StringComparison.OrdinalIgnoreCase));

    private static string Field(CsvRow header, CsvRow row, string column)
    {
        var index = IndexOf(header, column);
        return index < 0 || index >= row.Fields.Count ? string.Empty : row.Fields[index];
    }
}
