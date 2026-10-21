using System.Globalization;

namespace KryptoSteuer.Application.Prices;

/// <summary>
/// Parser für die EZB-Referenzkurse (Datei <c>eurofxref-hist.csv</c>): erste Zeile mit Spaltennamen (<c>Date,USD,JPY,...</c>),
/// danach je Tag eine Zeile, neueste zuerst; nicht veröffentlichte Werte stehen als <c>N/A</c>.
/// </summary>
public static class EcbRates
{
    public sealed record Rate(DateOnly Date, decimal UsdPerEur);

    public static IReadOnlyList<Rate> ParseUsd(string csv)
    {
        var lines = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return [];

        var header = lines[0].Split(',').Select(h => h.Trim()).ToList();
        var dateIndex = header.FindIndex(h => h.Equals("Date", StringComparison.OrdinalIgnoreCase));
        var usdIndex = header.FindIndex(h => h.Equals("USD", StringComparison.OrdinalIgnoreCase));
        if (dateIndex < 0 || usdIndex < 0)
            throw new FormatException("Die Datei enthält keine Spalten 'Date' und 'USD' (erwartet: EZB eurofxref-hist.csv).");

        var rates = new List<Rate>();
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');
            if (cells.Length <= Math.Max(dateIndex, usdIndex)) continue;
            if (!DateOnly.TryParseExact(cells[dateIndex].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (!decimal.TryParse(cells[usdIndex].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var usd) || usd <= 0) continue;
            rates.Add(new Rate(date, usd));
        }

        return rates.OrderBy(r => r.Date).ToList();
    }
}
