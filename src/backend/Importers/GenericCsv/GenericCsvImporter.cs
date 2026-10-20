using System.Globalization;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Importers.Common;

namespace KryptoSteuer.Importers.GenericCsv;

/// <summary>
/// Allgemeines CSV-Format für Broker ohne eigenen Importer und als Referenz für neue Importer
/// (siehe docs/04-importe.md). Spalten:
/// <c>id,timestamp,type,asset,amount,counter_asset,counter_amount,fee_asset,fee</c>.
/// Bei Buy, Sell und Trade ist <c>asset/amount</c> das Erhaltene und <c>counter_asset/counter_amount</c> das Gegebene.
/// </summary>
public sealed class GenericCsvImporter : IImporter
{
    public const string SourceName = "generic-csv";

    private static readonly string[] Required = ["timestamp", "type", "asset", "amount"];

    public string Source => SourceName;

    public bool CanHandle(ImportInput input)
    {
        var header = CsvReader.Read(input.Content).FirstOrDefault();
        return header is not null && Required.All(r => IndexOf(header, r) >= 0);
    }

    public IEnumerable<RawRecord> Parse(ImportInput input, ImportContext context)
    {
        CsvRow? header = null;
        foreach (var row in CsvReader.Read(input.Content))
        {
            if (header is null)
            {
                header = row;
                var missing = Required.Where(r => IndexOf(row, r) < 0).ToList();
                if (missing.Count > 0)
                {
                    context.AddError(row.LineNumber, $"Pflichtspalten fehlen: {string.Join(", ", missing)}.");
                    yield break;
                }

                continue;
            }

            context.RowsRead++;
            if (row.Fields.Count != header.Fields.Count)
            {
                context.AddError(row.LineNumber,
                    $"{row.Fields.Count} Spalten statt {header.Fields.Count}.");
                continue;
            }

            // Der Rohinhalt enthält Kopf- und Datenzeile, damit er allein auswertbar bleibt.
            var payload = header.RawText + "\n" + row.RawText;
            var id = Field(header, row, "id");
            yield return new RawRecord
            {
                Source = Source,
                ExternalId = string.IsNullOrWhiteSpace(id) ? null : id.Trim(),
                Payload = payload,
                FileHash = context.FileHash,
                ContentHash = IdempotencyKey.Sha256Hex(payload),
                LineNumber = row.LineNumber,
                ImportRunId = context.ImportRunId,
                ImportedAtUtc = context.ImportedAtUtc,
            };
        }
    }

    public IReadOnlyList<Transaction> Normalize(RawRecord record, ImportContext context)
    {
        var rows = CsvReader.Read(System.Text.Encoding.UTF8.GetBytes(record.Payload)).ToList();
        var header = rows[0];
        var row = rows[1];

        var timestamp = ParseTimestamp(Field(header, row, "timestamp"));
        var typeText = Field(header, row, "type").Trim();

        var asset = Field(header, row, "asset").Trim();
        var amount = ParseAmount(Field(header, row, "amount"), "amount");

        var legs = new List<TransactionLeg>();
        var type = ParseType(typeText);

        if (type is null)
        {
            // Nie stillschweigend verwerfen: als NeedsReview mit dem ursprünglichen Typ speichern.
            context.AddUnknownType(typeText);
            var unknownLeg = amount == 0 || string.IsNullOrWhiteSpace(asset)
                ? []
                : new List<TransactionLeg> { Leg(context, asset, Math.Abs(amount), amount < 0 ? LegDirection.Out : LegDirection.In) };
            return [Build(record, context, TransactionType.Unknown, timestamp, unknownLeg,
                TransactionStatus.NeedsReview, $"Unbekannter Typ '{typeText}'")];
        }

        if (string.IsNullOrWhiteSpace(asset))
            throw new ImportRowException("Spalte asset ist leer.");

        switch (type.Value)
        {
            case TransactionType.Buy:
            case TransactionType.Sell:
            case TransactionType.Trade:
                var counterAsset = Field(header, row, "counter_asset").Trim();
                if (string.IsNullOrWhiteSpace(counterAsset))
                    throw new ImportRowException($"{type} braucht counter_asset und counter_amount.");
                var counterAmount = ParseAmount(Field(header, row, "counter_amount"), "counter_amount");
                legs.Add(Leg(context, asset, RequirePositive(amount, "amount"), LegDirection.In));
                legs.Add(Leg(context, counterAsset, RequirePositive(counterAmount, "counter_amount"), LegDirection.Out));
                break;

            case TransactionType.Withdrawal:
            case TransactionType.TransferOut:
            case TransactionType.Fee:
            case TransactionType.Spend:
            case TransactionType.GiftOut:
                legs.Add(Leg(context, asset, RequirePositive(amount, "amount"), LegDirection.Out));
                break;

            case TransactionType.FuturesPnL:
            case TransactionType.FundingFee:
            case TransactionType.Liquidation:
                // Vorzeichen bestimmt die Richtung: Gewinn = In, Verlust = Out.
                if (amount == 0) throw new ImportRowException("Spalte amount darf nicht 0 sein.");
                legs.Add(Leg(context, asset, Math.Abs(amount), amount > 0 ? LegDirection.In : LegDirection.Out));
                break;

            default:
                legs.Add(Leg(context, asset, RequirePositive(amount, "amount"), LegDirection.In));
                break;
        }

        var feeText = Field(header, row, "fee");
        if (!string.IsNullOrWhiteSpace(feeText))
        {
            var fee = ParseAmount(feeText, "fee");
            if (fee != 0)
            {
                var feeAsset = Field(header, row, "fee_asset").Trim();
                if (string.IsNullOrWhiteSpace(feeAsset))
                    throw new ImportRowException("fee ohne fee_asset.");
                legs.Add(Leg(context, feeAsset, RequirePositive(fee, "fee"), LegDirection.Out, isFee: true));
            }
        }

        return [Build(record, context, type.Value, timestamp, legs, TransactionStatus.Ok, null)];
    }

    private static Transaction Build(
        RawRecord record, ImportContext context, TransactionType type, DateTimeOffset timestamp,
        List<TransactionLeg> legs, TransactionStatus status, string? note) => new()
    {
        Type = type,
        TimestampUtc = timestamp,
        AccountId = context.AccountId,
        Source = record.Source,
        ExternalId = record.ExternalId,
        ContentHash = record.ContentHash,
        Status = status,
        Note = note,
        Legs = legs,
        RawRecordIds = [record.Id],
    };

    private static TransactionLeg Leg(
        ImportContext context, string symbol, decimal amount, LegDirection direction, bool isFee = false) =>
        new() { AssetId = context.Assets.Resolve(symbol), Amount = amount, Direction = direction, IsFee = isFee };

    private static TransactionType? ParseType(string text)
    {
        var name = text.Replace("_", "", StringComparison.Ordinal);
        // Nur Namen, keine Zahlen: Enum.TryParse würde auch "5" akzeptieren.
        return name.Length > 0 && name.All(char.IsLetter)
            && Enum.TryParse<TransactionType>(name, ignoreCase: true, out var type)
            && type != TransactionType.Unknown
                ? type
                : null;
    }

    private static DateTimeOffset ParseTimestamp(string text)
    {
        // Ohne Zeitzonenangabe gilt UTC; Angaben mit Offset werden nach UTC umgerechnet.
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
            throw new ImportRowException($"Ungültiger Zeitstempel '{text}' (erwartet ISO 8601).");
        return value.ToUniversalTime();
    }

    private static decimal ParseAmount(string text, string column)
    {
        if (!decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new ImportRowException($"Ungültige Zahl in Spalte {column}: '{text}' (Punkt als Dezimaltrenner).");
        return value;
    }

    private static decimal RequirePositive(decimal value, string column) =>
        value > 0 ? value : throw new ImportRowException($"Spalte {column} muss größer als 0 sein.");

    private static int IndexOf(CsvRow header, string column) =>
        header.Fields.ToList().FindIndex(h => string.Equals(h.Trim(), column, StringComparison.OrdinalIgnoreCase));

    private static string Field(CsvRow header, CsvRow row, string column)
    {
        var index = IndexOf(header, column);
        return index < 0 || index >= row.Fields.Count ? string.Empty : row.Fields[index];
    }
}
