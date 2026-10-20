using System.Globalization;
using System.Text;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Importers.Common;

namespace KryptoSteuer.Importers.Bitget;

/// <summary>
/// Bitget-Export "Futures transactions" (Datei <c>Exported_USDT-M_Futures_transactions-….csv</c>, englische Oberfläche):
/// Spalten <c>Order, Date, Coin, Futures, Margin Mode, Type, Amount, Fee, Wallet balance</c>.
/// Besonderheiten: Zeilen stehen neueste zuerst, die Order-Nummer beginnt mit einem Tabulator, Gebühren sind negativ
/// und der Kontostand ändert sich um <c>Amount + Fee</c>. Die Zeitstempel stehen in der Zeitzone des Kontos.
/// Weitere Exporte (Spot-Orders, Ein-/Auszahlungen, Earn) folgen, sobald Beispieldateien vorliegen (docs/04-importe.md).
/// </summary>
public sealed class BitgetCsvImporter : IImporter
{
    public const string SourceName = "bitget-csv";
    public const string TimeZoneOption = "timeZone";

    private const string OffsetKey = "bitget-csv.offset";
    private const decimal BalanceTolerance = 0.000001m;

    private static readonly string[] FuturesColumns =
        ["Order", "Date", "Coin", "Futures", "Margin Mode", "Type", "Amount", "Fee", "Wallet balance"];

    /// <summary>Buchungsarten, die den Kontostand nicht ändern (Margin-Umverteilung), obwohl ein Betrag steht.</summary>
    private static bool IsWalletNeutral(string type) => type.StartsWith("adjust", StringComparison.OrdinalIgnoreCase);

    public string Source => SourceName;

    public bool CanHandle(ImportInput input)
    {
        var header = CsvReader.Read(input.Content).FirstOrDefault();
        return header is not null && FuturesColumns.All(c => IndexOf(header, c) >= 0);
    }

    // ---- Parse -------------------------------------------------------------------------------------------

    public IEnumerable<RawRecord> Parse(ImportInput input, ImportContext context)
    {
        var all = CsvReader.Read(input.Content).ToList();
        if (all.Count == 0) yield break;

        var header = all[0];
        var missing = FuturesColumns.Where(c => IndexOf(header, c) < 0).ToList();
        if (missing.Count > 0)
        {
            context.AddError(header.LineNumber, $"Spalten fehlen: {string.Join(", ", missing)}.");
            yield break;
        }

        var rows = new List<(CsvRow Row, string Id, string Type, DateTime? Local, decimal? Amount, decimal? Fee, decimal? Balance)>();
        foreach (var row in all.Skip(1))
        {
            context.RowsRead++;
            if (row.Fields.Count != header.Fields.Count)
            {
                context.AddError(row.LineNumber, $"{row.Fields.Count} Spalten statt {header.Fields.Count}.");
                continue;
            }

            var id = Field(header, row, "Order").Trim();
            var type = Field(header, row, "Type").Trim();
            if (id.Length == 0)
            {
                context.AddError(row.LineNumber, "Order-Nummer fehlt.");
                continue;
            }

            rows.Add((row, id, type, ParseLocal(Field(header, row, "Date")),
                ParseDec(Field(header, row, "Amount")), ParseDec(Field(header, row, "Fee")), ParseDec(Field(header, row, "Wallet balance"))));
        }

        var offset = ResolveOffset(input, context, rows.Where(r => r.Type.Contains("settle_fee", StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Local is not null).Select(r => r.Local!.Value).ToList());
        if (offset is null)
            yield break;
        context.Items[OffsetKey] = offset.Value;

        CheckBalances(header, rows, context);

        foreach (var r in rows)
        {
            var payload = header.RawText + "\n" + r.Row.RawText;
            yield return new RawRecord
            {
                Source = Source,
                ExternalId = $"futures:{r.Id}",
                Payload = payload,
                FileHash = context.FileHash,
                ContentHash = IdempotencyKey.Sha256Hex(payload),
                LineNumber = r.Row.LineNumber,
                ImportRunId = context.ImportRunId,
                ImportedAtUtc = context.ImportedAtUtc,
            };
        }
    }

    private static TimeSpan? ResolveOffset(ImportInput input, ImportContext context, List<DateTime> fundingTimes)
    {
        if (input.Options is not null && input.Options.TryGetValue(TimeZoneOption, out var requested) && !string.IsNullOrWhiteSpace(requested))
        {
            if (ExportTimeZone.TryParse(requested, out var chosen))
            {
                context.AddWarning($"Zeitzone der Datei laut Option: UTC{ExportTimeZone.Format(chosen)}.");
                return chosen;
            }

            context.AddError(null, $"Unbekannte Zeitzone '{requested}' (erlaubt z. B. +02:00, UTC+2, UTC).");
            return null;
        }

        var detected = ExportTimeZone.DetectFromFundingTimes(fundingTimes);
        if (detected is null)
        {
            context.AddError(null,
                "Zeitzone der Datei nicht erkennbar (zu wenige Funding-Zeilen). Bitte die Option timeZone angeben, z. B. +02:00.");
            return null;
        }

        context.AddWarning(
            $"Zeitzone der Datei aus den Funding-Zeitpunkten als UTC{ExportTimeZone.Format(detected.Value)} abgeleitet " +
            "(Bitget rechnet Funding um 00:00, 08:00 und 16:00 UTC ab). Bei Abweichung die Option timeZone angeben.");
        return detected;
    }

    /// <summary>Prüft die Kontostand-Spalte: lückenlos von alt nach neu? Zeilen fehlen sonst vermutlich in der Datei.</summary>
    private static void CheckBalances(
        CsvRow header,
        List<(CsvRow Row, string Id, string Type, DateTime? Local, decimal? Amount, decimal? Fee, decimal? Balance)> rows,
        ImportContext context)
    {
        var datable = rows.Where(r => r.Local is not null && r.Amount is not null && r.Fee is not null && r.Balance is not null).ToList();
        if (datable.Count < 2) return;

        // Der Export steht neueste zuerst; chronologisch ist die umgekehrte Dateireihenfolge (gleiche Sekunde: Dateireihenfolge).
        if (datable[0].Local >= datable[^1].Local) datable.Reverse();

        var first = datable[0];
        var firstDelta = IsWalletNeutral(first.Type) ? 0m : first.Amount!.Value + first.Fee!.Value;
        if (Math.Abs(first.Balance!.Value - firstDelta) > BalanceTolerance)
            context.AddWarning(
                $"Die Datei beginnt nicht bei Kontostand 0 (erste Zeile: Kontostand {first.Balance.Value.ToString(CultureInfo.InvariantCulture)}). " +
                "Der Export ist möglicherweise unvollständig (früherer Zeitraum fehlt).");

        var gaps = new List<string>();
        for (var i = 1; i < datable.Count; i++)
        {
            var previous = datable[i - 1].Balance!.Value;
            var row = datable[i];
            var expected = previous + (IsWalletNeutral(row.Type) ? 0m : row.Amount!.Value + row.Fee!.Value);
            if (Math.Abs(expected - row.Balance!.Value) > BalanceTolerance)
                gaps.Add($"Zeile {row.Row.LineNumber} ({row.Local:yyyy-MM-dd HH:mm:ss}, {row.Type}): erwartet " +
                         $"{expected.ToString(CultureInfo.InvariantCulture)}, Datei {row.Balance.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (gaps.Count > 0)
            context.AddWarning(
                $"Kontostand nicht lückenlos an {gaps.Count} Stellen, es fehlen vermutlich Zeilen oder Zeiträume im Export: " +
                string.Join("; ", gaps.Take(5)) + (gaps.Count > 5 ? "; …" : "."));
    }

    // ---- Normalize ---------------------------------------------------------------------------------------

    public IReadOnlyList<Transaction> Normalize(RawRecord record, ImportContext context)
    {
        var rows = CsvReader.Read(Encoding.UTF8.GetBytes(record.Payload)).ToList();
        var header = rows[0];
        var row = rows[1];

        if (!context.Items.TryGetValue(OffsetKey, out var offsetObj) || offsetObj is not TimeSpan offset)
            throw new ImportRowException("Zeitzone der Datei ist nicht bestimmt.");

        var local = ParseLocal(Field(header, row, "Date"))
            ?? throw new ImportRowException($"Ungültiges Datum '{Field(header, row, "Date")}' (erwartet yyyy-MM-dd HH:mm:ss).");
        var time = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset).ToUniversalTime();

        var amount = ParseDec(Field(header, row, "Amount")) ?? throw new ImportRowException($"Ungültiger Betrag '{Field(header, row, "Amount")}'.");
        var fee = ParseDec(Field(header, row, "Fee")) ?? throw new ImportRowException($"Ungültige Gebühr '{Field(header, row, "Fee")}'.");
        var coin = Field(header, row, "Coin").Trim();
        if (coin.Length == 0) throw new ImportRowException("Spalte Coin ist leer.");

        var builder = new BitgetTxBuilder(record, context);
        var tx = BitgetFuturesMapper.Map(builder, Field(header, row, "Type").Trim(), coin, amount, fee, time, Field(header, row, "Futures").Trim());
        return [tx];
    }

    // ---- Hilfen ------------------------------------------------------------------------------------------

    private static DateTime? ParseLocal(string text) =>
        DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal? ParseDec(string text) =>
        decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int IndexOf(CsvRow header, string column) =>
        header.Fields.ToList().FindIndex(h => string.Equals(h.Trim(), column, StringComparison.OrdinalIgnoreCase));

    private static string Field(CsvRow header, CsvRow row, string column)
    {
        var index = IndexOf(header, column);
        return index < 0 || index >= row.Fields.Count ? string.Empty : row.Fields[index];
    }
}
