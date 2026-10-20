using System.Globalization;

namespace KryptoSteuer.Reports;

/// <summary>Deutsches Zahlen- und Datumsformat für alle Berichte.</summary>
internal static class ReportFormat
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Eur(decimal v) => v.ToString("N2", De);

    public static string Qty(decimal v) => v.ToString("#,##0.##########", De);

    public static string Date(DateOnly d) => d.ToString("dd.MM.yyyy", De);

    public static string Stamp(DateTimeOffset t) => t.ToString("dd.MM.yyyy HH:mm 'UTC'", De);

    public static string Berlin(DateTimeOffset t) => KryptoSteuer.TaxEngine.Time.BerlinTime.DateOf(t).ToString("dd.MM.yyyy", De);

    public static string YesNo(bool v) => v ? "ja" : "nein";

    public static string Status(bool draft, int findings) =>
        draft ? $"ENTWURF: {findings} offene Befunde, Ergebnis noch nicht belastbar" : "final (keine offenen Befunde)";

    public const string FreigrenzeHinweis23 =
        "Freigrenze: Beträgt der Gesamtgewinn aus privaten Veräußerungsgeschäften im Jahr weniger als der Freigrenzenbetrag, bleibt er steuerfrei; " +
        "wird er erreicht oder überschritten, ist der gesamte Gewinn steuerpflichtig.";

    public const string FreigrenzeHinweis22 =
        "Freigrenze: Betragen die Einkünfte aus sonstigen Leistungen im Jahr weniger als der Freigrenzenbetrag, sind sie steuerfrei; " +
        "bei Erreichen oder Überschreiten ist der gesamte Betrag steuerpflichtig.";

    public const string KapHinweis =
        "Futures/Termingeschäfte (z. B. bei Bitget) gehören nicht zu § 23, sondern gelten als Einkünfte aus Kapitalvermögen (Anlage KAP, ausländischer Anbieter ohne inländischen Steuerabzug). " +
        "Die frühere Verlustverrechnungsgrenze von 20.000 EUR für Termingeschäfte ist durch das Jahressteuergesetz 2024 rückwirkend ab 2024 entfallen; für Jahre bis 2023 galt sie noch. " +
        "Funding- und Handelsgebühren sind in der Summe berücksichtigt. Die steuerliche Einordnung von Krypto-Futures bitte mit dem Steuerberater oder Finanzamt klären.";

    public const string WertpapiereHinweis =
        "Wertpapiere (z. B. Trade Republic): Die Jahressteuerbescheinigung des Brokers ist maßgeblich. Eine Anlage KAP ist nur nötig, wenn Erträge dort nicht oder " +
        "unvollständig versteuert wurden (z. B. Günstigerprüfung, Verlustbescheinigung). Diese App wertet Wertpapiere noch nicht aus (P20).";

    public const string Disclaimer = "Berechnungshilfe, keine Steuerberatung. Bitte die Werte vor der Abgabe prüfen.";
}
