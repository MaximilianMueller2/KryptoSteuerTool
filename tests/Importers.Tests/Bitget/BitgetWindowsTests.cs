using KryptoSteuer.Importers.Bitget;

namespace KryptoSteuer.Importers.Tests.Bitget;

public class BitgetWindowsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Zerlegt_90_Tage_in_drei_Bloecke_zu_30_Tagen()
    {
        var windows = BitgetWindows.Split(Now.AddDays(-90), Now);

        windows.Should().HaveCount(3);
        windows.Should().OnlyContain(w => w.To - w.From <= TimeSpan.FromDays(30));
        windows[0].From.Should().Be(Now.AddDays(-90));
        windows[^1].To.Should().Be(Now);
        for (var i = 1; i < windows.Count; i++) windows[i].From.Should().Be(windows[i - 1].To, "lückenlos");
    }

    [Fact]
    public void Begrenzt_aeltere_Startzeitpunkte_auf_90_Tage()
    {
        var windows = BitgetWindows.Split(Now.AddDays(-400), Now);

        windows[0].From.Should().Be(Now.AddDays(-90));
        windows.Should().HaveCount(3);
    }

    [Fact]
    public void Kurzer_Zeitraum_ergibt_ein_Fenster()
    {
        var windows = BitgetWindows.Split(Now.AddDays(-2), Now);

        windows.Should().ContainSingle().Which.Should().Be((Now.AddDays(-2), Now));
    }

    [Fact]
    public void Leerer_Zeitraum_ergibt_keine_Fenster()
    {
        BitgetWindows.Split(Now, Now).Should().BeEmpty();
        BitgetWindows.Split(Now.AddDays(1), Now).Should().BeEmpty();
    }
}
