using System.Text;
using KryptoSteuer.Api.Prices;
using KryptoSteuer.Application.Accounts;
using KryptoSteuer.Application.Prices;
using KryptoSteuer.Application.Tax;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.TaxEngine.Rules;
using Microsoft.AspNetCore.Mvc;

namespace KryptoSteuer.Api.Endpoints;

public sealed record ManualPriceRequest(string Symbol, DateTimeOffset At, decimal EurPrice, string? Source);

public sealed record PriceResponse(Guid AssetId, DateTimeOffset At, decimal EurPrice, string Source);

public static class TaxEndpoints
{
    public static IEndpointRouteBuilder MapTaxEndpoints(this IEndpointRouteBuilder app)
    {
        var tax = app.MapGroup("/api/tax").WithTags("Steuer");

        tax.MapPost("/runs", async (TaxRunRequest request, TaxRunService service, CancellationToken cancellationToken) =>
            {
                if (request.Year is < 2009 or > 2100)
                    return Results.Problem(detail: "Steuerjahr außerhalb des gültigen Bereichs.", statusCode: StatusCodes.Status400BadRequest);
                return Results.Ok(await service.RunAsync(request, cancellationToken));
            })
            .WithName("RunTax")
            .WithSummary("Berechnungslauf für ein Steuerjahr (speichert Parameter, Eingabe-Hash und Ergebnis-Snapshot; zeigt die Änderungen zum letzten Lauf)")
            .Produces<TaxRunResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        tax.MapGet("/runs/{id:guid}", async (Guid id, TaxRunService service, CancellationToken cancellationToken) =>
                await service.GetAsync(id, cancellationToken) is { } run ? Results.Ok(run) : Results.NotFound())
            .WithName("GetTaxRun")
            .WithSummary("Gespeicherten Berechnungslauf lesen")
            .Produces<TaxRunResult>()
            .Produces(StatusCodes.Status404NotFound);

        // Einzelaufstellung aller Veräußerungen eines Jahres (live aus den Buchungen, deterministisch).
        tax.MapGet("/years/{year:int}/disposals.csv", async (
                int year, bool? reform, ConsumptionMethod? method, TaxRunService service, IAccountRepository accounts, CancellationToken cancellationToken) =>
            {
                var report = await BuildReportAsync(year, reform, method, service, accounts, cancellationToken);
                // BOM, damit Excel UTF-8 erkennt.
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(report.ToCsv())).ToArray();
                return Results.File(bytes, "text/csv; charset=utf-8", $"einzelaufstellung-{year}.csv");
            })
            .WithName("DisposalsCsv")
            .WithSummary("Einzelaufstellung der Veräußerungen eines Jahres als CSV (deutsches Zahlenformat)")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        tax.MapGet("/years/{year:int}/disposals", async (
                int year, bool? reform, ConsumptionMethod? method, TaxRunService service, IAccountRepository accounts, CancellationToken cancellationToken) =>
                Results.Ok(await BuildReportAsync(year, reform, method, service, accounts, cancellationToken)))
            .WithName("Disposals")
            .WithSummary("Einzelaufstellung der Veräußerungen eines Jahres (JSON, auf Cent gerundet)")
            .Produces<DisposalReport>();

        tax.MapGet("/holdings", async (TaxRunService service, IAccountRepository accounts, TimeProvider clock, CancellationToken cancellationToken) =>
            {
                // Bestand heute: alle Buchungen, ohne Jahresgrenze.
                var computed = await service.ComputeAsync(new TaxRunRequest { Year = clock.GetUtcNow().Year }, cancellationToken);
                var names = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id, a => a.Name);
                return Results.Ok(HoldingsReport.Build(computed.Result, computed.Input.Assets, names));
            })
            .WithName("Holdings")
            .WithSummary("Aktueller Bestand je Konto und Asset (offene Lots, Kosten in EUR)")
            .Produces<IReadOnlyList<HoldingView>>();

        var prices = app.MapGroup("/api/prices").WithTags("Kurse");

        prices.MapPost("/ecb", async (IFormFile file, PriceService service, CancellationToken cancellationToken) =>
            {
                await using var stream = file.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                try
                {
                    return Results.Ok(await service.ImportEcbAsync(EcbRatesClient.ExtractCsv(buffer.ToArray()), cancellationToken));
                }
                catch (Exception ex) when (ex is FormatException or InvalidDataException)
                {
                    return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .DisableAntiforgery()
            .WithName("ImportEcbRates")
            .WithSummary("EZB-Referenzkurse importieren (Datei eurofxref-hist.csv oder .zip); EUR-Kurs für USD-Stablecoins")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<EcbImportResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        prices.MapPost("/ecb/fetch", async (EcbRatesClient client, PriceService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.ImportEcbAsync(await client.FetchCsvAsync(cancellationToken), cancellationToken));
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or FormatException or TaskCanceledException)
                {
                    return Results.Problem(detail: $"Abruf der EZB-Kurse fehlgeschlagen: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithName("FetchEcbRates")
            .WithSummary("EZB-Referenzkurse online abrufen und importieren")
            .Produces<EcbImportResult>()
            .ProducesProblem(StatusCodes.Status502BadGateway);

        prices.MapPut("/", async (ManualPriceRequest request, PriceService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    var p = await service.SetManualAsync(request.Symbol, request.At, request.EurPrice, request.Source, cancellationToken);
                    return Results.Ok(new PriceResponse(p.AssetId, p.TimestampUtc, p.EurPrice, p.Source));
                }
                catch (ArgumentException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .WithName("SetManualPrice")
            .WithSummary("EUR-Kurs von Hand eintragen (mit Quelle)")
            .Produces<PriceResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        prices.MapGet("/", async (string? symbol, IPriceStore store, CancellationToken cancellationToken) =>
            {
                Guid? assetId = null;
                if (!string.IsNullOrWhiteSpace(symbol))
                    assetId = (await store.GetAssetsAsync(cancellationToken)).FirstOrDefault(a => a.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))?.Id;
                var list = await store.ListAsync(assetId, cancellationToken);
                return Results.Ok(list.Select(p => new PriceResponse(p.AssetId, p.TimestampUtc, p.EurPrice, p.Source)));
            })
            .WithName("ListPrices")
            .WithSummary("Gespeicherte EUR-Kurse (höchstens 5.000)")
            .Produces<IEnumerable<PriceResponse>>();

        return app;
    }

    private static async Task<DisposalReport> BuildReportAsync(
        int year, bool? reform, ConsumptionMethod? method, TaxRunService service, IAccountRepository accounts, CancellationToken cancellationToken)
    {
        var request = new TaxRunRequest { Year = year, ApplyReform2027 = reform ?? false, Method = method ?? ConsumptionMethod.Fifo };
        var computed = await service.ComputeAsync(request, cancellationToken);
        var names = (await accounts.ListAsync(cancellationToken)).ToDictionary(a => a.Id, a => a.Name);
        return DisposalReport.Build(year, computed.Result, computed.Input.Assets, names);
    }
}
