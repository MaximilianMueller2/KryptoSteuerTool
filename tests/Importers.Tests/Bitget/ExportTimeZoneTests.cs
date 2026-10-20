using KryptoSteuer.Importers.Bitget;

namespace KryptoSteuer.Importers.Tests.Bitget;

public class ExportTimeZoneTests
{
    private static IReadOnlyCollection<DateTime> Funding(int hourOffset, params int[] baseHours) =>
        [.. Enumerable.Range(1, 6).SelectMany(day => baseHours.Select(h =>
            new DateTime(2026, 3, day, (h + hourOffset) % 24, 0, 3)))];

    [Theory]
    [InlineData(2, 2)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    [InlineData(3, 3)]
    public void Erkennt_die_Verschiebung_aus_Funding_um_00_08_16_UTC(int offsetHours, int expected)
    {
        var local = Enumerable.Range(1, 6).SelectMany(day => new[] { 0, 8, 16 }.Select(h =>
            new DateTime(2026, 3, day, 0, 0, 3).AddHours(h + offsetHours))).ToList();

        ExportTimeZone.DetectFromFundingTimes(local).Should().Be(TimeSpan.FromHours(expected));
    }

    [Fact]
    public void Vier_Stunden_Funding_liefert_dieselbe_Verschiebung()
    {
        var local = Enumerable.Range(1, 6).SelectMany(day => new[] { 0, 4, 8, 12, 16, 20 }.Select(h =>
            new DateTime(2026, 3, day, 0, 0, 3).AddHours(h + 2))).ToList();

        ExportTimeZone.DetectFromFundingTimes(local).Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Zu_wenige_oder_unregelmaessige_Zeilen_liefern_keine_Erkennung()
    {
        ExportTimeZone.DetectFromFundingTimes([new DateTime(2026, 3, 1, 2, 0, 3), new DateTime(2026, 3, 1, 10, 0, 3)]).Should().BeNull();
        ExportTimeZone.DetectFromFundingTimes(
            [new DateTime(2026, 3, 1, 2, 17, 3), new DateTime(2026, 3, 1, 10, 41, 3), new DateTime(2026, 3, 1, 18, 29, 3), new DateTime(2026, 3, 2, 2, 5, 3)])
            .Should().BeNull();
    }

    [Theory]
    [InlineData("+02:00", 2)]
    [InlineData("+2", 2)]
    [InlineData("UTC+2", 2)]
    [InlineData("UTC+02:00", 2)]
    [InlineData("GMT-5", -5)]
    [InlineData("-03:30", -3.5)]
    [InlineData("UTC", 0)]
    [InlineData("Z", 0)]
    [InlineData(" +01:00 ", 1)]
    public void Liest_gaengige_Schreibweisen(string text, double hours)
    {
        ExportTimeZone.TryParse(text, out var offset).Should().BeTrue();
        offset.Should().Be(TimeSpan.FromHours(hours));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mars")]
    [InlineData("+25:00")]
    [InlineData("UTC+99")]
    [InlineData("+2:7")]
    public void Lehnt_ungueltige_Angaben_ab(string text) => ExportTimeZone.TryParse(text, out _).Should().BeFalse();

    [Fact]
    public void Formatiert_die_Verschiebung() =>
        (ExportTimeZone.Format(TimeSpan.FromHours(2)), ExportTimeZone.Format(TimeSpan.FromHours(-3.5)), ExportTimeZone.Format(TimeSpan.Zero))
            .Should().Be(("+02:00", "-03:30", "+00:00"));
}
