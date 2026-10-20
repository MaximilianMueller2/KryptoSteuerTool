using ClosedXML.Excel;
using KryptoSteuer.Application.Reports;

namespace KryptoSteuer.Reports;

/// <summary>Excel-Bericht: ein Blatt je Abschnitt; Beträge als Zahlen mit deutschem Format (zwei Nachkommastellen), Summen als Werte der gerundeten Zeilen.</summary>
public static class XlsxReportRenderer
{
    private const string EurFormat = "#,##0.00";
    private const string QtyFormat = "#,##0.##########";

    public static byte[] Render(TaxReportData data)
    {
        using var wb = new XLWorkbook();
        Cover(wb.AddWorksheet("Deckblatt"), data);
        Summary(wb.AddWorksheet("Zusammenfassung"), data);
        Disposals(wb.AddWorksheet("Veräußerungen"), data);
        Incomes(wb.AddWorksheet("Laufende Einkünfte"), data);
        Holdings(wb.AddWorksheet("Bestand 31.12."), data);
        Audit(wb.AddWorksheet("Korrekturen"), data);
        Sources(wb.AddWorksheet("Quellen"), data);

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Header(IXLWorksheet ws, int row, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++) ws.Cell(row, i + 1).Value = titles[i];
        var range = ws.Range(row, 1, row, titles.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    private static void Eur(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = EurFormat;
    }

    private static void Pair(IXLWorksheet ws, ref int row, string label, string value)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 2).Value = value;
        row++;
    }

    private static void Cover(IXLWorksheet ws, TaxReportData d)
    {
        var c = d.Cover;
        var row = 1;
        ws.Cell(row++, 1).Value = $"Steuerreport {c.Year}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        row++;
        Pair(ws, ref row, "Name", c.TaxpayerName ?? "(nicht angegeben)");
        Pair(ws, ref row, "Steuerjahr", c.Year.ToString(ReportFormat.De));
        Pair(ws, ref row, "Erstellt am", ReportFormat.Stamp(c.CreatedAtUtc));
        Pair(ws, ref row, "Regelsatz", c.RuleSet + (c.Reform2027 ? " + Reform-Entwurf 2027 (nicht beschlossen)" : string.Empty));
        Pair(ws, ref row, "Verbrauchsfolge", c.Method == "Fifo" ? "FIFO je Konto" : c.Method);
        Pair(ws, ref row, "Kursquellen", c.PriceSources.Count == 0 ? "–" : string.Join("; ", c.PriceSources));
        Pair(ws, ref row, "Status", ReportFormat.Status(c.IsDraft, c.OpenFindings));
        Pair(ws, ref row, "Hash der Eingabedaten", c.InputHash);
        row++;
        ws.Cell(row, 1).Value = ReportFormat.Disclaimer;
        ws.Column(1).Width = 28;
        ws.Column(2).Width = 80;
    }

    private static void Summary(IXLWorksheet ws, TaxReportData d)
    {
        var row = 1;
        void Section(string title) { ws.Cell(row, 1).Value = title; ws.Cell(row, 1).Style.Font.Bold = true; row++; }
        void Line(string label, decimal value) { ws.Cell(row, 1).Value = label; Eur(ws.Cell(row, 2), value); row++; }
        void Note(string text) { ws.Cell(row, 1).Value = text; ws.Cell(row, 1).Style.Font.Italic = true; row += 2; }

        Section("Anlage SO – private Veräußerungsgeschäfte (andere Wirtschaftsgüter, § 23 EStG), nur steuerpflichtige Veräußerungen");
        Line("Veräußerungspreis (EUR)", d.So23.ProceedsEur);
        Line("Anschaffungskosten (EUR)", d.So23.CostEur);
        Line("Werbungskosten / Gebühren (EUR)", d.So23.FeesEur);
        Line("Gewinn/Verlust (EUR)", d.So23.GainEur);
        Line($"Steuerpflichtig nach Freigrenze und Verlustvortrag ({ReportFormat.Eur(d.So23.FreigrenzeEur)} EUR Freigrenze)", d.So23.TaxableEur);
        Note(ReportFormat.FreigrenzeHinweis23);

        Section("Anlage SO – Einkünfte aus sonstigen Leistungen (§ 22 Nr. 3 EStG)");
        Line("Einnahmen (EUR)", d.So22.IncomeEur);
        Line("Werbungskosten (EUR)", d.So22.ExpensesEur);
        Line("Ergebnis (EUR)", d.So22.NetEur);
        Line($"Steuerpflichtig nach Freigrenze ({ReportFormat.Eur(d.So22.FreigrenzeEur)} EUR Freigrenze)", d.So22.TaxableEur);
        Note(ReportFormat.FreigrenzeHinweis22);

        Section("Anlage KAP – Termingeschäfte (Futures)");
        Line("Realisierte Gewinne (EUR)", Math.Round(d.Futures.RealizedGainsEur, 2, MidpointRounding.AwayFromZero));
        Line("Realisierte Verluste (EUR)", Math.Round(d.Futures.RealizedLossesEur, 2, MidpointRounding.AwayFromZero));
        Line("Funding-Gebühren netto (EUR)", Math.Round(d.Futures.FundingNetEur, 2, MidpointRounding.AwayFromZero));
        Line("Handelsgebühren (EUR)", Math.Round(d.Futures.TradingFeesEur, 2, MidpointRounding.AwayFromZero));
        Line("Liquidationen (EUR)", Math.Round(d.Futures.LiquidationEur, 2, MidpointRounding.AwayFromZero));
        Line("Ergebnis (EUR)", Math.Round(d.Futures.NetEur, 2, MidpointRounding.AwayFromZero));
        Note(ReportFormat.KapHinweis);

        Section("Wertpapiere");
        Note(ReportFormat.WertpapiereHinweis);

        if (d.OpenFindings.Count > 0)
        {
            Section("Offene Befunde (Ergebnis ist ein Entwurf)");
            foreach (var f in d.OpenFindings) { ws.Cell(row++, 1).Value = f; }
        }

        ws.Column(1).Width = 110;
        ws.Column(2).Width = 18;
    }

    private static void Disposals(IXLWorksheet ws, TaxReportData d)
    {
        Header(ws, 1, "Asset", "Konto", "Menge", "Angeschafft", "Veräußert", "Haltetage", "Anschaffungskosten EUR", "Erlös EUR", "Gebühren EUR",
            "Gewinn/Verlust EUR", "Steuerpflichtig", "Behandlung", "Kursquelle", "Transaktions-ID");
        var row = 2;
        foreach (var r in d.Disposals.Rows)
        {
            ws.Cell(row, 1).Value = r.Asset;
            ws.Cell(row, 2).Value = r.Account;
            ws.Cell(row, 3).Value = r.Quantity;
            ws.Cell(row, 3).Style.NumberFormat.Format = QtyFormat;
            ws.Cell(row, 4).Value = r.Acquired.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 4).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(row, 5).Value = r.Disposed.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 5).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(row, 6).Value = r.HoldingDays;
            Eur(ws.Cell(row, 7), r.CostEur);
            Eur(ws.Cell(row, 8), r.ProceedsEur);
            Eur(ws.Cell(row, 9), r.FeeEur);
            Eur(ws.Cell(row, 10), r.GainEur);
            ws.Cell(row, 11).Value = ReportFormat.YesNo(r.Taxable);
            ws.Cell(row, 12).Value = r.Treatment;
            ws.Cell(row, 13).Value = r.PriceSource;
            ws.Cell(row, 14).Value = r.TransactionId;
            row++;
        }

        row++;
        void Total(string label, decimal v) { ws.Cell(row, 1).Value = label; ws.Cell(row, 1).Style.Font.Bold = true; Eur(ws.Cell(row, 10), v); row++; }
        Total("Summe steuerpflichtig (Gewinne)", d.Disposals.TaxableGainEur);
        Total("Summe steuerpflichtig (Verluste)", d.Disposals.TaxableLossEur);
        Total("Summe steuerpflichtig (Netto)", d.Disposals.TaxableNetEur);
        Total("Summe steuerfrei nach Frist (Netto)", d.Disposals.TaxFreeNetEur);
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private static void Incomes(IXLWorksheet ws, TaxReportData d)
    {
        Header(ws, 1, "Datum", "Asset", "Konto", "Menge", "Wert EUR", "Art", "Zählt als Einkunft", "Kursquelle", "Transaktions-ID");
        var row = 2;
        foreach (var i in d.Incomes)
        {
            ws.Cell(row, 1).Value = i.Date.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(row, 2).Value = i.Asset;
            ws.Cell(row, 3).Value = i.Account;
            ws.Cell(row, 4).Value = i.Quantity;
            ws.Cell(row, 4).Style.NumberFormat.Format = QtyFormat;
            Eur(ws.Cell(row, 5), i.ValueEur);
            ws.Cell(row, 6).Value = i.Kind;
            ws.Cell(row, 7).Value = ReportFormat.YesNo(i.CountsAsIncome);
            ws.Cell(row, 8).Value = i.PriceSource;
            ws.Cell(row, 9).Value = i.TransactionId;
            row++;
        }

        row++;
        ws.Cell(row, 1).Value = "Summe Einkünfte";
        ws.Cell(row, 1).Style.Font.Bold = true;
        Eur(ws.Cell(row, 5), d.So22.IncomeEur);
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private static void Holdings(IXLWorksheet ws, TaxReportData d)
    {
        Header(ws, 1, "Konto", "Asset", "Angeschafft", "Restmenge", "Restkosten EUR", "Regime", "Steuerfrei ab");
        var row = 2;
        foreach (var h in d.Holdings)
        {
            ws.Cell(row, 1).Value = h.Account;
            ws.Cell(row, 2).Value = h.Asset;
            ws.Cell(row, 3).Value = h.Acquired.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 3).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(row, 4).Value = h.Quantity;
            ws.Cell(row, 4).Style.NumberFormat.Format = QtyFormat;
            Eur(ws.Cell(row, 5), h.CostEur);
            ws.Cell(row, 6).Value = h.Regime;
            ws.Cell(row, 7).Value = h.TaxFreeFrom.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 7).Style.DateFormat.Format = "dd.MM.yyyy";
            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private static void Audit(IXLWorksheet ws, TaxReportData d)
    {
        Header(ws, 1, "Zeitpunkt (UTC)", "Objekt", "Begründung", "Stand danach");
        var row = 2;
        foreach (var a in d.Audit)
        {
            ws.Cell(row, 1).Value = ReportFormat.Stamp(a.AtUtc);
            ws.Cell(row, 2).Value = a.Entity;
            ws.Cell(row, 3).Value = a.Reason;
            ws.Cell(row, 4).Value = a.After ?? "(entfernt)";
            row++;
        }

        if (d.Audit.Count == 0) ws.Cell(2, 1).Value = "Keine manuellen Korrekturen.";
        ws.Columns().AdjustToContents();
    }

    private static void Sources(IXLWorksheet ws, TaxReportData d)
    {
        Header(ws, 1, "Quelle", "Datei", "SHA-256", "Festgeschrieben", "Zeitraum von", "Zeitraum bis");
        var row = 2;
        foreach (var s in d.Sources)
        {
            ws.Cell(row, 1).Value = s.Source;
            ws.Cell(row, 2).Value = s.FileName;
            ws.Cell(row, 3).Value = s.FileHash;
            ws.Cell(row, 4).Value = s.CommittedAtUtc is { } c ? ReportFormat.Stamp(c) : "–";
            ws.Cell(row, 5).Value = s.From is { } f ? ReportFormat.Berlin(f) : "–";
            ws.Cell(row, 6).Value = s.To is { } t ? ReportFormat.Berlin(t) : "–";
            row++;
        }

        ws.Columns().AdjustToContents();
    }
}
