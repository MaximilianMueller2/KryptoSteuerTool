using KryptoSteuer.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KryptoSteuer.Reports;

/// <summary>PDF-Bericht (QuestPDF, A4 quer): Deckblatt, Zusammenfassung zum Eintragen, Einzelaufstellung, laufende Einkünfte, Bestand, Korrekturen, Quellen.</summary>
public static class PdfReportRenderer
{
    static PdfReportRenderer()
    {
        // QuestPDF Community-Lizenz: kostenlos für Privatpersonen und kleine Unternehmen.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Render(TaxReportData d)
    {
        var c = d.Cover;
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                Setup(page, c);
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text($"Steuerreport {c.Year}").FontSize(24).Bold();
                    col.Item().Text(c.TaxpayerName ?? "(Name nicht angegeben)").FontSize(14);
                    col.Item().PaddingTop(10).Element(e => KeyValues(e, [
                        ("Steuerjahr", c.Year.ToString(ReportFormat.De)),
                        ("Erstellt am", ReportFormat.Stamp(c.CreatedAtUtc)),
                        ("Regelsatz", c.RuleSet + (c.Reform2027 ? " + Reform-Entwurf 2027 (nicht beschlossen)" : string.Empty)),
                        ("Verbrauchsfolge", c.Method == "Fifo" ? "FIFO je Konto" : c.Method),
                        ("Kursquellen", c.PriceSources.Count == 0 ? "–" : string.Join("; ", c.PriceSources)),
                        ("Status", ReportFormat.Status(c.IsDraft, c.OpenFindings)),
                        ("Hash der Eingabedaten", c.InputHash),
                    ]));
                    if (c.IsDraft)
                    {
                        col.Item().PaddingTop(10).Text("Offene Befunde").Bold();
                        foreach (var f in d.OpenFindings.Take(30)) col.Item().Text("• " + f).FontSize(8);
                        if (d.OpenFindings.Count > 30) col.Item().Text($"… und {d.OpenFindings.Count - 30} weitere (siehe App, Seite Prüfung).").FontSize(8);
                    }
                });
            });

            doc.Page(page =>
            {
                Setup(page, c);
                page.Content().Column(col =>
                {
                    col.Spacing(4);
                    col.Item().Text("Zusammenfassung zum Eintragen").FontSize(16).Bold();

                    Heading(col, "Anlage SO – private Veräußerungsgeschäfte (andere Wirtschaftsgüter, § 23 EStG), nur steuerpflichtige Veräußerungen");
                    col.Item().Element(e => KeyValues(e, [
                        ("Veräußerungspreis", ReportFormat.Eur(d.So23.ProceedsEur) + " EUR"),
                        ("Anschaffungskosten", ReportFormat.Eur(d.So23.CostEur) + " EUR"),
                        ("Werbungskosten (Gebühren)", ReportFormat.Eur(d.So23.FeesEur) + " EUR"),
                        ("Gewinn/Verlust", ReportFormat.Eur(d.So23.GainEur) + " EUR"),
                        ($"Steuerpflichtig (Freigrenze {ReportFormat.Eur(d.So23.FreigrenzeEur)} EUR, nach Verlustvortrag)", ReportFormat.Eur(d.So23.TaxableEur) + " EUR"),
                    ]));
                    col.Item().Text(ReportFormat.FreigrenzeHinweis23).FontSize(8).Italic();

                    Heading(col, "Anlage SO – Einkünfte aus sonstigen Leistungen (§ 22 Nr. 3 EStG)");
                    col.Item().Element(e => KeyValues(e, [
                        ("Einnahmen", ReportFormat.Eur(d.So22.IncomeEur) + " EUR"),
                        ("Werbungskosten", ReportFormat.Eur(d.So22.ExpensesEur) + " EUR"),
                        ("Ergebnis", ReportFormat.Eur(d.So22.NetEur) + " EUR"),
                        ($"Steuerpflichtig (Freigrenze {ReportFormat.Eur(d.So22.FreigrenzeEur)} EUR)", ReportFormat.Eur(d.So22.TaxableEur) + " EUR"),
                    ]));
                    col.Item().Text(ReportFormat.FreigrenzeHinweis22).FontSize(8).Italic();

                    Heading(col, "Anlage KAP – Termingeschäfte (Futures)");
                    var f = d.Futures;
                    static string Rd(decimal v) => ReportFormat.Eur(Math.Round(v, 2, MidpointRounding.AwayFromZero)) + " EUR";
                    col.Item().Element(e => KeyValues(e, [
                        ("Realisierte Gewinne", Rd(f.RealizedGainsEur)), ("Realisierte Verluste", Rd(f.RealizedLossesEur)),
                        ("Funding-Gebühren netto", Rd(f.FundingNetEur)), ("Handelsgebühren", Rd(f.TradingFeesEur)),
                        ("Liquidationen", Rd(f.LiquidationEur)), ("Ergebnis", Rd(f.NetEur)),
                    ]));
                    col.Item().Text(ReportFormat.KapHinweis).FontSize(8).Italic();

                    Heading(col, "Wertpapiere");
                    col.Item().Text(ReportFormat.WertpapiereHinweis).FontSize(8).Italic();
                });
            });

            doc.Page(page =>
            {
                Setup(page, c);
                page.Content().Column(col =>
                {
                    col.Item().Text("Einzelaufstellung aller Veräußerungen").FontSize(16).Bold();
                    col.Item().PaddingTop(4).Element(e => Table(e,
                        ["Asset", "Konto", "Menge", "Angeschafft", "Veräußert", "Tage", "Kosten EUR", "Erlös EUR", "Gebühr EUR", "Gewinn EUR", "Pflichtig", "Kursquelle", "Transaktion"],
                        [1, 1.4f, 1.3f, 1.1f, 1.1f, 0.6f, 1.1f, 1.1f, 0.9f, 1.1f, 0.8f, 1, 2.2f],
                        d.Disposals.Rows.Select(r => new[]
                        {
                            r.Asset, r.Account, ReportFormat.Qty(r.Quantity), ReportFormat.Date(r.Acquired), ReportFormat.Date(r.Disposed), r.HoldingDays.ToString(ReportFormat.De),
                            ReportFormat.Eur(r.CostEur), ReportFormat.Eur(r.ProceedsEur), ReportFormat.Eur(r.FeeEur), ReportFormat.Eur(r.GainEur), ReportFormat.YesNo(r.Taxable),
                            r.PriceSource, r.TransactionId,
                        }).ToList(),
                        rightAligned: [2, 5, 6, 7, 8, 9]));
                    col.Item().PaddingTop(6).Element(e => KeyValues(e, [
                        ("Summe steuerpflichtig (Gewinne)", ReportFormat.Eur(d.Disposals.TaxableGainEur) + " EUR"),
                        ("Summe steuerpflichtig (Verluste)", ReportFormat.Eur(d.Disposals.TaxableLossEur) + " EUR"),
                        ("Summe steuerpflichtig (Netto)", ReportFormat.Eur(d.Disposals.TaxableNetEur) + " EUR"),
                        ("Summe steuerfrei nach Frist (Netto)", ReportFormat.Eur(d.Disposals.TaxFreeNetEur) + " EUR"),
                    ]));
                });
            });

            doc.Page(page =>
            {
                Setup(page, c);
                page.Content().Column(col =>
                {
                    col.Spacing(4);
                    col.Item().Text("Laufende Einkünfte (§ 22 Nr. 3 EStG)").FontSize(16).Bold();
                    if (d.Incomes.Count == 0) col.Item().Text("Keine laufenden Einkünfte in diesem Jahr.");
                    else
                        col.Item().Element(e => Table(e, ["Datum", "Asset", "Konto", "Menge", "Wert EUR", "Art", "Zählt", "Kursquelle", "Transaktion"], [1, 1, 1.4f, 1.3f, 1, 1.4f, 0.7f, 1.2f, 2],
                            d.Incomes.Select(i => new[]
                            {
                                ReportFormat.Date(i.Date), i.Asset, i.Account, ReportFormat.Qty(i.Quantity), ReportFormat.Eur(i.ValueEur), i.Kind,
                                ReportFormat.YesNo(i.CountsAsIncome), i.PriceSource, i.TransactionId,
                            }).ToList(), rightAligned: [3, 4]));

                    col.Item().PaddingTop(12).Text("Bestandsliste zum 31.12.").FontSize(16).Bold();
                    if (d.Holdings.Count == 0) col.Item().Text("Kein Bestand.");
                    else
                        col.Item().Element(e => Table(e, ["Konto", "Asset", "Angeschafft", "Restmenge", "Restkosten EUR", "Regime", "Steuerfrei ab"], [1.5f, 1, 1.1f, 1.3f, 1.2f, 0.8f, 1.1f],
                            d.Holdings.Select(h => new[]
                            {
                                h.Account, h.Asset, ReportFormat.Date(h.Acquired), ReportFormat.Qty(h.Quantity), ReportFormat.Eur(h.CostEur), h.Regime, ReportFormat.Date(h.TaxFreeFrom),
                            }).ToList(), rightAligned: [3, 4]));
                });
            });

            doc.Page(page =>
            {
                Setup(page, c);
                page.Content().Column(col =>
                {
                    col.Spacing(4);
                    col.Item().Text("Manuelle Korrekturen und Entscheidungen (Audit-Log)").FontSize(16).Bold();
                    if (d.Audit.Count == 0) col.Item().Text("Keine manuellen Korrekturen.");
                    else
                        col.Item().Element(e => Table(e, ["Zeitpunkt", "Objekt", "Begründung"], [1.4f, 1.2f, 4],
                            d.Audit.Select(a => new[] { ReportFormat.Stamp(a.AtUtc), a.Entity, a.Reason }).ToList()));

                    col.Item().PaddingTop(12).Text("Anhang: importierte Quellen").FontSize(16).Bold();
                    if (d.Sources.Count == 0) col.Item().Text("Keine Importe.");
                    else
                        col.Item().Element(e => Table(e, ["Quelle", "Datei", "SHA-256", "Zeitraum"], [1.2f, 2, 4, 1.8f],
                            d.Sources.Select(s => new[]
                            {
                                s.Source, s.FileName, s.FileHash,
                                (s.From is { } f ? ReportFormat.Berlin(f) : "–") + " bis " + (s.To is { } t ? ReportFormat.Berlin(t) : "–"),
                            }).ToList()));
                });
            });
        }).GeneratePdf();
    }

    private static void Setup(PageDescriptor page, ReportCover c)
    {
        page.Size(PageSizes.A4.Landscape());
        page.Margin(28);
        page.DefaultTextStyle(t => t.FontSize(9));
        page.Footer().Row(r =>
        {
            r.RelativeItem().Text($"Steuerreport {c.Year} · {(c.IsDraft ? "ENTWURF" : "final")} · Hash {c.InputHash[..12]} · {ReportFormat.Disclaimer}").FontSize(7);
            r.AutoItem().Text(t => { t.Span("Seite ").FontSize(7); t.CurrentPageNumber().FontSize(7); t.Span(" von ").FontSize(7); t.TotalPages().FontSize(7); });
        });
    }

    private static void Heading(ColumnDescriptor col, string text) => col.Item().PaddingTop(8).Text(text).Bold();

    private static void KeyValues(IContainer container, IReadOnlyList<(string Label, string Value)> rows) =>
        container.Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(4); });
            foreach (var (label, value) in rows)
            {
                t.Cell().PaddingVertical(1).Text(label).SemiBold();
                t.Cell().PaddingVertical(1).Text(value);
            }
        });

    private static void Table(IContainer container, string[] headers, float[] widths, IReadOnlyList<string[]> rows, int[]? rightAligned = null)
    {
        var right = rightAligned ?? [];
        container.Table(t =>
        {
            t.ColumnsDefinition(c => { foreach (var w in widths) c.RelativeColumn(w); });
            t.Header(h =>
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = h.Cell().Background(Colors.Grey.Lighten2).Padding(2);
                    (right.Contains(i) ? cell.AlignRight() : cell).Text(headers[i]).Bold().FontSize(7);
                }
            });
            foreach (var row in rows)
                for (var i = 0; i < row.Length; i++)
                {
                    var cell = t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten1).Padding(2);
                    (right.Contains(i) ? cell.AlignRight() : cell).Text(row[i]).FontSize(7);
                }
        });
    }
}
