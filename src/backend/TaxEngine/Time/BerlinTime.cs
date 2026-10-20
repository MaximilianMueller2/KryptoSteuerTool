namespace KryptoSteuer.TaxEngine.Time;

/// <summary>
/// Zeit in Europe/Berlin ohne Abhängigkeit von der Zeitzonen-Datenbank des Systems (mitteleuropäische Zeit, Sommerzeit
/// von letztem Sonntag im März 01:00 UTC bis letztem Sonntag im Oktober 01:00 UTC). Steuerliche Tagesgrenzen gelten in Berliner Zeit.
/// </summary>
public static class BerlinTime
{
    public static TimeSpan OffsetAt(DateTimeOffset utc) => IsSummerTime(utc) ? TimeSpan.FromHours(2) : TimeSpan.FromHours(1);

    public static bool IsSummerTime(DateTimeOffset utc)
    {
        var u = utc.ToUniversalTime();
        var start = new DateTimeOffset(LastSunday(u.Year, 3).AddHours(1), TimeSpan.Zero);
        var end = new DateTimeOffset(LastSunday(u.Year, 10).AddHours(1), TimeSpan.Zero);
        return u >= start && u < end;
    }

    /// <summary>Kalendertag in Berliner Zeit.</summary>
    public static DateOnly DateOf(DateTimeOffset utc) => DateOnly.FromDateTime((utc.ToUniversalTime() + OffsetAt(utc)).DateTime);

    public static int YearOf(DateTimeOffset utc) => DateOf(utc).Year;

    /// <summary>Berliner Ortszeit als UTC-Zeitpunkt (für feste Stichtage in Regelsätzen).</summary>
    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time);
        // Zuerst Winterzeit annehmen, dann prüfen, ob dieser Zeitpunkt in der Sommerzeit liegt.
        var asWinter = new DateTimeOffset(local, TimeSpan.FromHours(1)).ToUniversalTime();
        return IsSummerTime(asWinter) ? new DateTimeOffset(local, TimeSpan.FromHours(2)).ToUniversalTime() : asWinter;
    }

    private static DateTime LastSunday(int year, int month)
    {
        var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return d.AddDays(-(int)d.DayOfWeek);
    }
}
