using System.Globalization;
using System.Text;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Model;
using KryptoSteuer.TaxEngine.Time;

namespace KryptoSteuer.Application.Tax;

/// <summary>Eine Zeile der Einzelaufstellung, auf Cent gerundet (kaufmännisch, erst hier in der Ausgabe).</summary>
public sealed record DisposalRow(
    string Asset, string Account, decimal Quantity, DateOnly Acquired, DateOnly Disposed, int HoldingDays,
    decimal CostEur, decimal ProceedsEur, decimal FeeEur, decimal GainEur, bool Taxable, string Treatment, string PriceSource, string TransactionId);

public sealed record DisposalReport(int Year, IReadOnlyList<DisposalRow> Rows, decimal TaxableGainEur, decimal TaxableLossEur, decimal TaxableNetEur, decimal TaxFreeNetEur)
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Einzelaufstellung aller Veräußerungen eines Jahres. Die Summen sind die Summen der gerundeten Zeilen (stimmen auf den Cent überein).</summary>
    public static DisposalReport Build(
        int year, TaxEngineResult result, IReadOnlyDictionary<Guid, Asset> assets, IReadOnlyDictionary<Guid, string> accountNames)
    {
        static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        var rows = result.Disposals.Where(d => d.TaxYear == year)
            .Where(d => d.Treatment != TaxTreatment.NetworkFeeNeutral)
            .OrderBy(d => d.TimestampUtc).ThenBy(d => d.TransactionExternalId, StringComparer.Ordinal)
            .Select(d => new DisposalRow(
                assets.TryGetValue(d.AssetId, out var a) ? a.Symbol : d.AssetId.ToString("N")[..8],
                accountNames.GetValueOrDefault(d.AccountId, d.AccountId.ToString("N")[..8]),
                d.Quantity, BerlinTime.DateOf(d.AcquiredAtUtc), BerlinTime.DateOf(d.TimestampUtc), d.HoldingDays,
                R(d.CostEur), R(d.ProceedsEur), R(d.FeeEur), R(d.ProceedsEur) - R(d.CostEur) - R(d.FeeEur),
                d.Treatment is TaxTreatment.Section23Taxable or TaxTreatment.ReformCapital, d.Treatment.ToString(), d.PriceSource,
                d.TransactionExternalId ?? d.TransactionId.ToString("N")[..8]))
            .ToList();

        var taxable = rows.Where(r => r.Taxable).ToList();
        return new DisposalReport(year, rows,
            taxable.Where(r => r.GainEur > 0).Sum(r => r.GainEur), taxable.Where(r => r.GainEur < 0).Sum(r => r.GainEur),
            taxable.Sum(r => r.GainEur), rows.Where(r => !r.Taxable).Sum(r => r.GainEur));
    }

    /// <summary>CSV mit Semikolon und deutschem Zahlenformat (Komma als Dezimaltrenner), UTF-8 mit BOM für Excel.</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.Append("Asset;Konto;Menge;Anschaffung;Veräußerung;Haltetage;Anschaffungskosten EUR;Erlös EUR;Gebühren EUR;Gewinn/Verlust EUR;steuerpflichtig;Behandlung;Kursquelle;Transaktions-ID\n");
        foreach (var r in Rows)
            sb.Append(Cell(r.Asset)).Append(';').Append(Cell(r.Account)).Append(';')
              .Append(r.Quantity.ToString("0.##########", De)).Append(';')
              .Append(r.Acquired.ToString("dd.MM.yyyy", De)).Append(';').Append(r.Disposed.ToString("dd.MM.yyyy", De)).Append(';')
              .Append(r.HoldingDays.ToString(De)).Append(';')
              .Append(r.CostEur.ToString("N2", De)).Append(';').Append(r.ProceedsEur.ToString("N2", De)).Append(';')
              .Append(r.FeeEur.ToString("N2", De)).Append(';').Append(r.GainEur.ToString("N2", De)).Append(';')
              .Append(r.Taxable ? "ja" : "nein").Append(';').Append(Cell(r.Treatment)).Append(';').Append(Cell(r.PriceSource)).Append(';')
              .Append(Cell(r.TransactionId)).Append('\n');
        sb.Append("Summe steuerpflichtig (Gewinne);;;;;;;;;").Append(TaxableGainEur.ToString("N2", De)).Append('\n');
        sb.Append("Summe steuerpflichtig (Verluste);;;;;;;;;").Append(TaxableLossEur.ToString("N2", De)).Append('\n');
        sb.Append("Summe steuerpflichtig (Netto);;;;;;;;;").Append(TaxableNetEur.ToString("N2", De)).Append('\n');
        sb.Append("Summe steuerfrei nach Frist (Netto);;;;;;;;;").Append(TaxFreeNetEur.ToString("N2", De)).Append('\n');
        return sb.ToString();
    }

    private static string Cell(string value) =>
        value.Contains(';') || value.Contains('"') || value.Contains('\n') ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
