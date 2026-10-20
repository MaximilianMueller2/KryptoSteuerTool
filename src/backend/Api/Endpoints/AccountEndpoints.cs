using KryptoSteuer.Application.Accounts;
using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Api.Endpoints;

public sealed record CreateAccountRequest(
    string Name, AccountType Type, string Platform, string? Country, string? SubAccount);

public sealed record AccountResponse(
    Guid Id, string Name, AccountType Type, string Platform, string? Country, string? SubAccount)
{
    public static AccountResponse From(Account a) => new(a.Id, a.Name, a.Type, a.Platform, a.Country, a.SubAccount);
}

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").WithTags("Accounts");

        group.MapGet("/", async (AccountUseCases accounts, CancellationToken cancellationToken) =>
                (await accounts.ListAsync(cancellationToken)).Select(AccountResponse.From))
            .WithName("ListAccounts")
            .WithSummary("Alle Konten (Wallets, Börsen, Depots)");

        group.MapPost("/", async (CreateAccountRequest request, AccountUseCases accounts, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Platform))
                    return Results.Problem(detail: "Name und Plattform sind Pflicht.", statusCode: StatusCodes.Status400BadRequest);

                var account = await accounts.CreateAsync(
                    request.Name, request.Type, request.Platform, request.Country, request.SubAccount, cancellationToken);
                return Results.Created($"/api/accounts/{account.Id}", AccountResponse.From(account));
            })
            .WithName("CreateAccount")
            .WithSummary("Konto anlegen")
            .Produces<AccountResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
