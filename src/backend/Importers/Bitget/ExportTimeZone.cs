using System.Globalization;

namespace KryptoSteuer.Importers.Bitget;

/// <summary>Zeitzone einer Exportdatei: feste Verschiebung zu UTC (Bitget zeigt Zeiten in der im Konto gewählten Zeitzone).</summary>
public static class ExportTimeZone
{
    /// <summary>Liest "+02:00", "+2", "UTC+2", "UTC+02:00", "GMT-5", "UTC", "Z" oder (falls auf dem System bekannt) eine IANA-Kennung.</summary>
    public static bool TryParse(string? text, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();

        if (t.Equals("UTC", StringComparison.OrdinalIgnoreCase) || t.Equals("Z", StringComparison.OrdinalIgnoreCase)
            || t.Equals("GMT", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var prefix in new[] { "UTC", "GMT" })
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                t = t[prefix.Length..];

        if (t.Length > 0 && (t[0] == '+' || t[0] == '-'))
        {
            var sign = t[0] == '-' ? -1 : 1;
            var body = t[1..];
            if (!body.Contains(':') && int.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out var hours) && hours <= 14)
            {
                offset = TimeSpan.FromHours(sign * hours);
                return true;
            }

            if (TimeSpan.TryParseExact(body, @"hh\:mm", CultureInfo.InvariantCulture, out var span) && span <= TimeSpan.FromHours(14))
            {
                offset = sign * span;
                return true;
            }

            return false;
        }

        try
        {
            // Nur sinnvoll ohne Sommerzeit; mit IANA-Kennung gilt der Offset am angegebenen Zeitpunkt nicht, deshalb die aktuelle Standardzeit.
            offset = TimeZoneInfo.FindSystemTimeZoneById(text.Trim()).BaseUtcOffset;
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static string Format(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Leitet die Zeitzone aus den Funding-Zeitpunkten ab: Bitget rechnet Funding um 00:00, 08:00 und 16:00 UTC ab
    /// (Verträge mit 4-Stunden-Funding liegen auf denselben Zeitpunkten). Das häufigste "Stunde modulo 8" ergibt die Verschiebung;
    /// mehrdeutig sind Vielfache von 8 Stunden, dann gilt der Wert, der Mitteleuropa am nächsten liegt.
    /// Liefert <c>null</c>, wenn zu wenige oder zu unregelmäßige Funding-Zeilen vorliegen.
    /// </summary>
    public static TimeSpan? DetectFromFundingTimes(IReadOnlyCollection<DateTime> fundingLocalTimes)
    {
        if (fundingLocalTimes.Count < 3) return null;

        // Die Abrechnung läuft Sekunden bis wenige Minuten nach der vollen Stunde.
        var onTheHour = fundingLocalTimes.Where(d => d.Minute < 5).ToList();
        if (onTheHour.Count < fundingLocalTimes.Count * 0.8) return null;

        // Zuerst "Stunde modulo 8" (Funding bei 00/08/16 UTC). Mischen sich 4-Stunden-Verträge so ein, dass kein Wert
        // deutlich überwiegt, genügt "Stunde modulo 4"; die Zeitzone ist dann nur bis auf Vielfache von 4 Stunden bestimmt.
        var by8 = onTheHour.GroupBy(d => d.Hour % 8).OrderByDescending(g => g.Count()).First();
        if (by8.Count() >= onTheHour.Count * 0.6)
            return NearestToCentralEurope(by8.Key, step: 8);

        var by4 = onTheHour.GroupBy(d => d.Hour % 4).OrderByDescending(g => g.Count()).First();
        return by4.Count() >= onTheHour.Count * 0.9 ? NearestToCentralEurope(by4.Key, step: 4) : null;
    }

    private static TimeSpan NearestToCentralEurope(int remainder, int step)
    {
        var candidates = Enumerable.Range(-12, 27).Where(h => ((h - remainder) % step + step) % step == 0);
        return TimeSpan.FromHours(candidates.OrderBy(h => Math.Abs(h - 1.5)).First());
    }
}
