namespace KryptoSteuer.TaxEngine.Rules;

/// <summary>Die hinterlegten Regelsätze. Rechtsgrundlagen siehe docs/05-steuerregeln.md.</summary>
public static class RuleSetCatalog
{
    public const string DeEStG23Id = "DE-23EStG";
    public const string DeReform2027Id = "DE-Reform-2027-Entwurf";

    /// <summary>Geltendes Recht: private Veräußerungsgeschäfte (§ 23 EStG) und sonstige Einkünfte (§ 22 Nr. 3 EStG).</summary>
    public static RuleSet DeEStG23 { get; } = new(
        DeEStG23Id, "2026.1", "Geltendes Recht: § 23 und § 22 Nr. 3 EStG",
        "geltendes Recht",
        [
            new("HoldingPeriodYears", 1, 1900, null, "§ 23 Abs. 1 Satz 1 Nr. 2 EStG", "https://www.gesetze-im-internet.de/estg/__23.html",
                "Veräußerung steuerfrei, wenn zwischen Anschaffung und Veräußerung mehr als ein Jahr liegt; Frist nach §§ 187, 188 BGB."),
            new("Freigrenze23", 600, 1900, 2023, "§ 23 Abs. 3 Satz 5 EStG", "https://www.gesetze-im-internet.de/estg/__23.html",
                "Freigrenze: Gewinn unter 600 EUR steuerfrei, ab 600 EUR voll steuerpflichtig (bis Veranlagungszeitraum 2023)."),
            new("Freigrenze23", 1000, 2024, null, "§ 23 Abs. 3 Satz 5 EStG", "https://www.gesetze-im-internet.de/estg/__23.html",
                "Freigrenze: Gewinn unter 1.000 EUR steuerfrei, ab 1.000 EUR voll steuerpflichtig (ab Veranlagungszeitraum 2024)."),
            new("Freigrenze22Nr3", 256, 1900, null, "§ 22 Nr. 3 Satz 2 EStG", "https://www.gesetze-im-internet.de/estg/__22.html",
                "Freigrenze: Einkünfte aus sonstigen Leistungen unter 256 EUR im Kalenderjahr steuerfrei."),
        ]);

    /// <summary>
    /// Referentenentwurf (Stand 08.09.2026, nicht beschlossen): gilt nur für Lots mit Regime "Neu" und nur, wenn der Schalter
    /// "Reform 2027 anwenden" eingeschaltet ist (Standard: aus). Kapitalvermögen statt § 23, keine Haltefrist.
    /// </summary>
    public static RuleSet DeReform2027 { get; } = new(
        DeReform2027Id, "2026-09-08", "Referentenentwurf ab 2027 (nicht beschlossen)",
        "Entwurf, nicht beschlossen",
        [
            new("CutoffYear", 2027, 2027, null, "Referentenentwurf vom 08.09.2026", "https://www.kleinstb.de",
                "Neubestand: Tauschkryptowerte, angeschafft oder zugeflossen nach dem 31.12.2026 23:59:59 Europe/Berlin."),
            new("FlatTaxRate", 0.25m, 2027, null, "Referentenentwurf vom 08.09.2026", "https://www.kleinstb.de", "Abgeltungsteuer 25 %."),
            new("SolidarityRate", 0.055m, 2027, null, "§ 4 SolzG", "https://www.gesetze-im-internet.de/solzg_1995/__4.html",
                "Solidaritätszuschlag 5,5 % auf die Abgeltungsteuer (26,375 % insgesamt)."),
            new("SparerPauschbetragSingle", 1000, 2027, null, "§ 20 Abs. 9 EStG", "https://www.gesetze-im-internet.de/estg/__20.html",
                "Sparer-Pauschbetrag für Einzelveranlagung."),
            new("SparerPauschbetragJoint", 2000, 2027, null, "§ 20 Abs. 9 EStG", "https://www.gesetze-im-internet.de/estg/__20.html",
                "Sparer-Pauschbetrag für Zusammenveranlagung."),
        ]);

    public static IReadOnlyList<RuleSet> All { get; } = [DeEStG23, DeReform2027];
}
