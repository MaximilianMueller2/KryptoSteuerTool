using KryptoSteuer.Application.Bitget;
using KryptoSteuer.Application.Secrets;
using KryptoSteuer.Domain.Ledger;
using KryptoSteuer.Importers.Bitget;

namespace KryptoSteuer.Importers.Tests.Bitget;

internal sealed class FakeSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => "enc(" + new string(plaintext.Reverse().ToArray()) + ")";

    public string Unprotect(string protectedValue) =>
        new(protectedValue["enc(".Length..^1].Reverse().ToArray());
}

internal sealed class FakeBitgetClientFactory(FakeBitgetClient client) : IBitgetClientFactory
{
    public List<BitgetCredentials> Created { get; } = [];

    public IBitgetClient Create(BitgetCredentials credentials)
    {
        Created.Add(credentials);
        return client;
    }
}

/// <summary>Speicher für Zugangsdaten, Sync-Zustand und Review-Einträge im Arbeitsspeicher; Bestände aus dem <see cref="InMemoryImportStore"/>.</summary>
internal sealed class InMemoryBitgetSyncStore(InMemoryImportStore importStore) : IBitgetSyncStore
{
    public Dictionary<Guid, ExchangeCredential> Credentials { get; } = [];
    public Dictionary<Guid, Dictionary<string, DateTimeOffset>> States { get; } = [];
    public List<(Guid AccountId, IReadOnlyList<BalanceMismatch> Mismatches)> AppliedMismatches { get; } = [];

    public Task<ExchangeCredential?> GetCredentialAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(Credentials.GetValueOrDefault(accountId));

    public Task SaveCredentialAsync(ExchangeCredential credential, CancellationToken cancellationToken)
    {
        Credentials[credential.AccountId] = credential;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteCredentialAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(Credentials.Remove(accountId));

    public Task<IReadOnlyList<Guid>> AccountsWithCredentialsAsync(string provider, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. Credentials.Keys]);

    public Task<IReadOnlyDictionary<string, DateTimeOffset>> GetSyncStatesAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, DateTimeOffset>>(
            new Dictionary<string, DateTimeOffset>(States.GetValueOrDefault(accountId) ?? []));

    public Task SetSyncStatesAsync(Guid accountId, IReadOnlyDictionary<string, DateTimeOffset> states, CancellationToken cancellationToken)
    {
        if (!States.TryGetValue(accountId, out var map)) States[accountId] = map = [];
        foreach (var (k, v) in states) map[k] = v;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, decimal>> GetHoldingsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var symbols = importStore.Assets.ToDictionary(a => a.Id, a => a.Symbol);
        var holdings = importStore.Transactions
            .Where(t => t.AccountId == accountId && t.Status != TransactionStatus.Ignored)
            .SelectMany(t => t.Legs)
            .GroupBy(l => symbols[l.AssetId])
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Direction == LegDirection.In ? l.Amount : -l.Amount));
        return Task.FromResult<IReadOnlyDictionary<string, decimal>>(holdings);
    }

    public Task ApplyBalanceMismatchesAsync(
        Guid accountId, IReadOnlyList<BalanceMismatch> mismatches, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        AppliedMismatches.Add((accountId, mismatches));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReviewItem>> ListOpenReviewItemsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReviewItem>>([]);
}
