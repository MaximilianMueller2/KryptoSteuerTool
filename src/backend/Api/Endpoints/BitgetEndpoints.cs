using KryptoSteuer.Application.Bitget;
using KryptoSteuer.Importers.Bitget;

namespace KryptoSteuer.Api.Endpoints;

public sealed record BitgetCredentialsRequest(string ApiKey, string Secret, string Passphrase);

public sealed record BitgetStatusResponse(
    bool Configured, string? AccountType, IReadOnlyList<string> Warnings, DateTimeOffset? LastSyncAtUtc, string? LastSyncSummary);

public sealed record ReviewItemResponse(
    Guid Id, string Kind, string Reason, Guid? AccountId, Guid? AssetId, string? DetailsJson, DateTimeOffset CreatedAtUtc);

public static class BitgetEndpoints
{
    public static IEndpointRouteBuilder MapBitgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts/{accountId:guid}/bitget").WithTags("Bitget");

        // Key, Secret und Passphrase werden geprüft (Kontotyp, Rechte) und nur verschlüsselt gespeichert; nie zurückgegeben.
        group.MapPut("/", async (Guid accountId, BitgetCredentialsRequest request, BitgetSyncService sync, CancellationToken cancellationToken) =>
            {
                try
                {
                    var check = await sync.SaveCredentialsAsync(
                        accountId, new BitgetCredentials(request.ApiKey, request.Secret, request.Passphrase), cancellationToken);
                    return Results.Ok(new BitgetStatusResponse(true, check.AccountType, check.Warnings, null, null));
                }
                catch (BitgetSyncException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .WithName("SaveBitgetCredentials")
            .WithSummary("Bitget-API-Key hinterlegen (nur Leserechte empfohlen; Warnung bei weitergehenden Rechten)")
            .Produces<BitgetStatusResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/", async (Guid accountId, IBitgetSyncStore store, CancellationToken cancellationToken) =>
            {
                var credential = await store.GetCredentialAsync(accountId, cancellationToken);
                return credential is null
                    ? Results.Ok(new BitgetStatusResponse(false, null, [], null, null))
                    : Results.Ok(new BitgetStatusResponse(
                        true, credential.AccountType,
                        credential.PermissionWarnings?.Split('\n') ?? [],
                        credential.LastSyncAtUtc, credential.LastSyncSummary));
            })
            .WithName("GetBitgetStatus")
            .WithSummary("Status der Bitget-Anbindung eines Kontos")
            .Produces<BitgetStatusResponse>();

        group.MapDelete("/", async (Guid accountId, IBitgetSyncStore store, CancellationToken cancellationToken) =>
                await store.DeleteCredentialAsync(accountId, cancellationToken) ? Results.NoContent() : Results.NotFound())
            .WithName("DeleteBitgetCredentials")
            .WithSummary("Bitget-Key und Sync-Zustand entfernen (importierte Buchungen bleiben)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/sync", async (Guid accountId, BitgetSyncService sync, CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await sync.SyncAsync(accountId, cancellationToken));
                }
                catch (BitgetSyncException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
                }
            })
            .WithName("SyncBitget")
            .WithSummary("Jetzt synchronisieren: abrufen, importieren, Salden abgleichen")
            .Produces<BitgetSyncResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/review-items", async (IBitgetSyncStore store, CancellationToken cancellationToken) =>
                (await store.ListOpenReviewItemsAsync(cancellationToken)).Select(r => new ReviewItemResponse(
                    r.Id, r.Kind.ToString(), r.Reason, r.AccountId, r.AssetId, r.DetailsJson, r.CreatedAtUtc)))
            .WithTags("Review")
            .WithName("ListReviewItems")
            .WithSummary("Offene Einträge der Review-Queue (z. B. Saldo-Abweichungen)");

        return app;
    }
}
