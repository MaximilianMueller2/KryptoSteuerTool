using KryptoSteuer.TaxEngine.Rules;

namespace KryptoSteuer.TaxEngine.Tests;

public class RuleSetTests
{
    private static readonly RuleSet Law = RuleSetCatalog.DeEStG23;

    [Theory]
    [InlineData(2020, 600)]
    [InlineData(2023, 600)]
    [InlineData(2024, 1000)]
    [InlineData(2026, 1000)]
    [InlineData(2030, 1000)]
    public void Freigrenze_23_wird_je_Steuerjahr_gewaehlt(int year, int expected) =>
        Law.Get("Freigrenze23", year).Should().Be(expected);

    [Theory]
    [InlineData(2018)]
    [InlineData(2026)]
    public void Freigrenze_22_Nr_3_ist_256_Euro(int year) => Law.Get("Freigrenze22Nr3", year).Should().Be(256);

    [Fact]
    public void Haltefrist_ist_ein_Jahr() => Law.Get("HoldingPeriodYears", 2026).Should().Be(1);

    [Fact]
    public void Unbekannter_Parameter_oder_Jahr_ohne_Eintrag_wirft_statt_zu_raten()
    {
        var unknown = () => Law.Get("GibtEsNicht", 2026);
        var tooEarly = () => RuleSetCatalog.DeReform2027.Get("FlatTaxRate", 2026);

        unknown.Should().Throw<InvalidOperationException>();
        tooEarly.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Jeder_Parameter_hat_Rechtsgrundlage_und_Quelle()
    {
        foreach (var set in RuleSetCatalog.All)
            set.Parameters.Should().OnlyContain(p => p.LegalBasis.Length > 0 && p.SourceUrl.StartsWith("http", StringComparison.Ordinal));
    }

    [Fact]
    public void Reform_Entwurf_enthaelt_25_Prozent_Soli_und_Pauschbetraege()
    {
        var reform = RuleSetCatalog.DeReform2027;

        reform.Get("FlatTaxRate", 2027).Should().Be(0.25m);
        reform.Get("SolidarityRate", 2027).Should().Be(0.055m);
        reform.Get("SparerPauschbetragSingle", 2027).Should().Be(1000);
        reform.Get("SparerPauschbetragJoint", 2027).Should().Be(2000);
        reform.LegalStatus.Should().Contain("nicht beschlossen");
    }

    [Fact]
    public void JSON_Snapshot_ist_verlustfrei_lesbar_und_enthaelt_Id_und_Version()
    {
        var json = Law.ToJson();
        var roundTrip = RuleSet.FromJson(json);

        json.Should().Contain("DE-23EStG").And.Contain("2026.1");
        roundTrip.Id.Should().Be(Law.Id);
        roundTrip.Version.Should().Be(Law.Version);
        roundTrip.Parameters.Should().BeEquivalentTo(Law.Parameters);
        roundTrip.Get("Freigrenze23", 2024).Should().Be(1000);
    }

    [Fact]
    public void Standard_Einstellungen_Reform_aus_FIFO_Netzwerkgebuehr_neutral()
    {
        var settings = new TaxSettings();

        settings.ApplyReform2027.Should().BeFalse();
        settings.Method.Should().Be(ConsumptionMethod.Fifo);
        settings.NetworkFees.Should().Be(NetworkFeeTreatment.NeutralWithoutProceeds);
        settings.TransferWindow.Should().Be(TimeSpan.FromHours(48));
        settings.IsTauschkrypto("BTC").Should().BeTrue();
    }
}
