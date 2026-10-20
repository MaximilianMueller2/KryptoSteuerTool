using KryptoSteuer.TaxEngine.Time;

namespace KryptoSteuer.TaxEngine.Tests;

public class HoldingPeriodTests
{
    [Fact]
    public void Kauf_15_03_2025_Frist_endet_15_03_2026_Verkauf_ab_16_03_2026_steuerfrei()
    {
        var bought = new DateOnly(2025, 3, 15);

        HoldingPeriod.LastTaxableDay(bought).Should().Be(new DateOnly(2026, 3, 15));
        HoldingPeriod.TaxFreeFrom(bought).Should().Be(new DateOnly(2026, 3, 16));
        HoldingPeriod.IsTaxFree(bought, new DateOnly(2026, 3, 15)).Should().BeFalse("am Jahrestag ist der Verkauf noch steuerpflichtig");
        HoldingPeriod.IsTaxFree(bought, new DateOnly(2026, 3, 16)).Should().BeTrue();
    }

    [Fact]
    public void Kauf_29_02_2024_Frist_endet_28_02_2025_steuerfrei_ab_01_03_2025()
    {
        var bought = new DateOnly(2024, 2, 29);

        HoldingPeriod.LastTaxableDay(bought).Should().Be(new DateOnly(2025, 2, 28));
        HoldingPeriod.TaxFreeFrom(bought).Should().Be(new DateOnly(2025, 3, 1));
        HoldingPeriod.IsTaxFree(bought, new DateOnly(2025, 2, 28)).Should().BeFalse();
        HoldingPeriod.IsTaxFree(bought, new DateOnly(2025, 3, 1)).Should().BeTrue();
    }

    [Fact]
    public void Kauf_am_31_12_endet_die_Frist_am_31_12_des_Folgejahres()
    {
        HoldingPeriod.TaxFreeFrom(new DateOnly(2026, 12, 31)).Should().Be(new DateOnly(2028, 1, 1));
    }

    [Fact]
    public void Haltetage_sind_die_Kalendertage_zwischen_Anschaffung_und_Veraeusserung()
    {
        HoldingPeriod.Days(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1)).Should().Be(0);
        HoldingPeriod.Days(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1)).Should().Be(365);
        HoldingPeriod.Days(new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1)).Should().Be(366);
    }

    [Fact]
    public void Kauf_kurz_vor_Mitternacht_UTC_zaehlt_nach_Berliner_Datum()
    {
        // 22:30 UTC am 14.03.2025 ist in Berlin (UTC+1) bereits der 14.03.2025 23:30; 23:30 UTC ist schon der 15.03.
        var acquiredBerlinDate = BerlinTime.DateOf(new DateTimeOffset(2025, 3, 14, 23, 30, 0, TimeSpan.Zero));

        acquiredBerlinDate.Should().Be(new DateOnly(2025, 3, 15));
        HoldingPeriod.TaxFreeFrom(acquiredBerlinDate).Should().Be(new DateOnly(2026, 3, 16));
    }
}
