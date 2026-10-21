using KryptoSteuer.TaxEngine.Time;

namespace KryptoSteuer.TaxEngine.Tests;

public class BerlinTimeTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, TimeSpan.Zero);

    [Theory]
    [InlineData(2026, 1, 15, 12, 1)]   // Winter: UTC+1
    [InlineData(2026, 7, 15, 12, 2)]   // Sommer: UTC+2
    [InlineData(2026, 3, 29, 0, 1)]    // Sommerzeit beginnt am letzten Sonntag im März um 01:00 UTC
    [InlineData(2026, 3, 29, 1, 2)]
    [InlineData(2026, 10, 25, 0, 2)]   // endet am letzten Sonntag im Oktober um 01:00 UTC
    [InlineData(2026, 10, 25, 1, 1)]
    [InlineData(2027, 3, 28, 1, 2)]
    [InlineData(2024, 3, 31, 1, 2)]
    public void Offset_folgt_den_EU_Sommerzeitregeln(int y, int mo, int d, int h, int expectedHours) =>
        BerlinTime.OffsetAt(Utc(y, mo, d, h)).Should().Be(TimeSpan.FromHours(expectedHours));

    [Fact]
    public void Stimmt_mit_der_Zeitzonen_Datenbank_ueberein_wenn_vorhanden()
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"); }
        catch (TimeZoneNotFoundException) { return; }

        for (var t = Utc(2023, 1, 1); t < Utc(2029, 1, 1); t = t.AddHours(1))
            BerlinTime.OffsetAt(t).Should().Be(tz.GetUtcOffset(t), $"bei {t:O}");
    }

    [Theory]
    [InlineData(2026, 12, 31, 22, 59, 59, 2026, 12, 31)] // 23:59:59 Berlin
    [InlineData(2026, 12, 31, 23, 0, 0, 2027, 1, 1)]     // 00:00:00 Berlin am 01.01.2027
    [InlineData(2025, 6, 30, 22, 0, 0, 2025, 7, 1)]      // Sommerzeit: Mitternacht Berlin = 22:00 UTC
    [InlineData(2025, 6, 30, 21, 59, 59, 2025, 6, 30)]
    [InlineData(2026, 3, 28, 23, 30, 0, 2026, 3, 29)]
    public void Kalendertag_in_Berliner_Zeit_bei_Mitternacht_UTC_gegen_Berlin(int y, int mo, int d, int h, int mi, int s, int ey, int em, int ed) =>
        BerlinTime.DateOf(Utc(y, mo, d, h, mi, s)).Should().Be(new DateOnly(ey, em, ed));

    [Fact]
    public void Jahr_wechselt_in_Berlin_eine_Stunde_vor_UTC()
    {
        BerlinTime.YearOf(Utc(2026, 12, 31, 23, 30)).Should().Be(2027);
        BerlinTime.YearOf(Utc(2026, 12, 31, 22, 59)).Should().Be(2026);
    }

    [Theory]
    [InlineData(2027, 1, 1, 0, 0, 0)]
    [InlineData(2025, 7, 1, 0, 0, 0)]
    [InlineData(2026, 3, 29, 12, 0, 0)]
    public void ToUtc_ist_die_Umkehrung_von_DateOf_am_Tagesanfang(int y, int mo, int d, int h, int mi, int s)
    {
        var date = new DateOnly(y, mo, d);
        var utc = BerlinTime.ToUtc(date, new TimeOnly(h, mi, s));

        BerlinTime.DateOf(utc).Should().Be(date);
        if (h == 0) BerlinTime.DateOf(utc.AddSeconds(-1)).Should().Be(date.AddDays(-1));
    }
}
