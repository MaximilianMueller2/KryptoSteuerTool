namespace KryptoSteuer.TaxEngine.Time;

/// <summary>
/// Haltefrist nach § 23 Abs. 1 Satz 1 Nr. 2 EStG i. V. m. §§ 187, 188 BGB auf Kalendertagen in Berliner Zeit.
/// Anschaffung am 15.03.2025: Frist endet 15.03.2026, ein Verkauf ab 16.03.2026 ist steuerfrei.
/// Anschaffung am 29.02.2024: Frist endet 28.02.2025, steuerfrei ab 01.03.2025.
/// </summary>
public static class HoldingPeriod
{
    public static DateOnly LastTaxableDay(DateOnly acquired, int years = 1) => acquired.AddYears(years);

    /// <summary>Erster Tag, an dem ein Verkauf steuerfrei ist ("steuerfrei ab").</summary>
    public static DateOnly TaxFreeFrom(DateOnly acquired, int years = 1) => LastTaxableDay(acquired, years).AddDays(1);

    public static bool IsTaxFree(DateOnly acquired, DateOnly sold, int years = 1) => sold >= TaxFreeFrom(acquired, years);

    public static int Days(DateOnly acquired, DateOnly sold) => sold.DayNumber - acquired.DayNumber;
}
