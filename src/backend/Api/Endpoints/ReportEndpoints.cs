using System.Text;
using KryptoSteuer.Application.Reports;
using KryptoSteuer.Application.Tax;
using KryptoSteuer.Reports;
using KryptoSteuer.TaxEngine.Rules;

namespace KryptoSteuer.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Berichte");

        group.MapGet("/{year:int}/pdf", async (int year, string? name, bool? reform, ConsumptionMethod? method, ReportService service, CancellationToken ct) =>
                Validate(year) ?? Results.File(PdfReportRenderer.Render(await Build(service, year, name, reform, method, ct)), "application/pdf", $"steuerreport-{year}.pdf"))
            .WithName("ReportPdf")
            .WithSummary("Steuerreport als PDF: Deckblatt, Zusammenfassung zum Eintragen, Einzelaufstellung, Einkünfte, Bestand, Korrekturen, Quellen")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{year:int}/xlsx", async (int year, string? name, bool? reform, ConsumptionMethod? method, ReportService service, CancellationToken ct) =>
                Validate(year) ?? Results.File(XlsxReportRenderer.Render(await Build(service, year, name, reform, method, ct)),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"steuerreport-{year}.xlsx"))
            .WithName("ReportXlsx")
            .WithSummary("Steuerreport als Excel-Datei (ein Blatt je Abschnitt)")
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{year:int}/csv", async (int year, string? name, bool? reform, ConsumptionMethod? method, ReportService service, CancellationToken ct) =>
            {
                if (Validate(year) is { } problem) return problem;
                var data = await Build(service, year, name, reform, method, ct);
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(data.Disposals.ToCsv())).ToArray();
                return Results.File(bytes, "text/csv; charset=utf-8", $"einzelaufstellung-{year}.csv");
            })
            .WithName("ReportCsv")
            .WithSummary("Einzelaufstellung der Veräußerungen als CSV (gleiche Zeilen und Summen wie PDF und Excel)")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static IResult? Validate(int year) =>
        year is < 2009 or > 2100 ? Results.Problem(detail: "Steuerjahr außerhalb des gültigen Bereichs.", statusCode: StatusCodes.Status400BadRequest) : null;

    private static Task<TaxReportData> Build(ReportService service, int year, string? name, bool? reform, ConsumptionMethod? method, CancellationToken ct) =>
        service.BuildAsync(new TaxRunRequest { Year = year, ApplyReform2027 = reform ?? false, Method = method ?? ConsumptionMethod.Fifo }, name, ct);
}
