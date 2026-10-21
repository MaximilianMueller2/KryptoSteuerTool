using KryptoSteuer.Application.Tax;

namespace KryptoSteuer.Api.Endpoints;

public sealed record OwnTransferRequest(Guid? TargetAccountId, string? NewAccountName, string? Note);

public sealed record NoteRequest(string? Note);

public static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/review").WithTags("Prüfung");

        group.MapGet("/", async (ReviewService service, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListReview")
            .WithSummary("Offene Befunde der Steuerberechnung mit möglichen Aktionen und bereits getroffene Entscheidungen")
            .Produces<ReviewList>();

        group.MapPut("/transfers/{transactionId:guid}/own-account", async (
                Guid transactionId, OwnTransferRequest request, ReviewService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.DecideOwnTransferAsync(transactionId, request.TargetAccountId, request.NewAccountName, request.Note, cancellationToken));
                }
                catch (ReviewException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .WithName("DecideOwnTransfer")
            .WithSummary("Auszahlung als steuerneutralen Transfer auf ein eigenes Konto markieren (bestehendes Konto oder neues Wallet-Konto)")
            .Produces<DecisionView>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/transactions/{transactionId:guid}/acknowledge", async (
                Guid transactionId, NoteRequest request, ReviewService service, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await service.AcknowledgeAsync(transactionId, request.Note, cancellationToken));
                }
                catch (ReviewException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .WithName("AcknowledgeTransaction")
            .WithSummary("Buchung zur Prüfung als geprüft bestätigen (ohne Wirkung auf die Berechnung)")
            .Produces<DecisionView>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/decisions/{transactionId:guid}", async (Guid transactionId, ReviewService service, CancellationToken cancellationToken) =>
                await service.RevokeAsync(transactionId, null, cancellationToken) ? Results.NoContent() : Results.NotFound())
            .WithName("RevokeDecision")
            .WithSummary("Entscheidung zurücknehmen (mit Audit-Eintrag)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
