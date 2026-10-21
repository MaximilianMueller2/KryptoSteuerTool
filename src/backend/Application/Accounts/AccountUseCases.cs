using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Application.Accounts;

public interface IAccountRepository
{
    Task AddAsync(Account account, CancellationToken cancellationToken);
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Konten anlegen und auflisten; Voraussetzung für Importe (jede Buchung gehört zu einem Konto).</summary>
public sealed class AccountUseCases(IAccountRepository repository)
{
    public async Task<Account> CreateAsync(
        string name, AccountType type, string platform, string? country, string? subAccount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(platform))
            throw new ArgumentException("Name und Plattform sind Pflicht.");

        var account = new Account
        {
            Name = name.Trim(), Type = type, Platform = platform.Trim(),
            Country = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant(),
            SubAccount = string.IsNullOrWhiteSpace(subAccount) ? null : subAccount.Trim(),
        };
        await repository.AddAsync(account, cancellationToken);
        return account;
    }

    public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken) =>
        repository.ListAsync(cancellationToken);
}
