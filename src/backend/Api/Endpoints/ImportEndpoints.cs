using KryptoSteuer.Application.Imports;
using Microsoft.AspNetCore.Mvc;

namespace KryptoSteuer.Api.Endpoints;

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").WithTags("Imports");

        // Datei-Upload: speichert die Datei roh, parst und liefert die Vorschau. Geschrieben wird noch nichts.
        group.MapPost("/", async (
                IFormFile file, [FromForm] Guid accountId, [FromForm] string? source,
                ImportService imports, CancellationToken cancellationToken) =>
            {
                await using var stream = file.OpenReadStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);

                return await Run(async () =>
                {
                    var run = await imports.PreviewAsync(
                        new ImportInput(Path.GetFileName(file.FileName), buffer.ToArray()), accountId, source, cancellationToken);
                    return Results.Created($"/api/imports/{run.Id}", run);
                });
            })
            .DisableAntiforgery()
            .WithName("CreateImport")
            .WithSummary("Datei hochladen und Importvorschau erzeugen (noch nichts festgeschrieben)")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ImportRunInfo>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}", (Guid id, ImportService imports, CancellationToken cancellationToken) =>
                Run(async () => Results.Ok(await imports.GetAsync(id, cancellationToken))))
            .WithName("GetImport")
            .WithSummary("Importbericht eines Laufs")
            .Produces<ImportRunInfo>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/commit", (Guid id, ImportService imports, CancellationToken cancellationToken) =>
                Run(async () => Results.Ok(await imports.CommitAsync(id, cancellationToken))))
            .WithName("CommitImport")
            .WithSummary("Vorschau bestätigen und Rohdaten sowie Buchungen festschreiben")
            .Produces<ImportRunInfo>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", (Guid id, ImportService imports, CancellationToken cancellationToken) =>
                Run(async () =>
                {
                    await imports.DeleteAsync(id, cancellationToken);
                    return Results.NoContent();
                }))
            .WithName("DeleteImport")
            .WithSummary("Unbestätigten Importlauf löschen")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ImportException ex)
        {
            var status = ex.Failure switch
            {
                ImportFailure.NotFound => StatusCodes.Status404NotFound,
                ImportFailure.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            return Results.Problem(detail: ex.Message, statusCode: status);
        }
    }
}
