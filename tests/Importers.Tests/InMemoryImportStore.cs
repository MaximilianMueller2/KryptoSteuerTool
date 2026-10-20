using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Importers.Tests;

/// <summary>Einfacher Speicher für Tests des Importablaufs, ohne Datenbank.</summary>
internal sealed class InMemoryImportStore : IImportStore
{
    public Guid AccountId { get; } = Guid.NewGuid();
    public List<Asset> Assets { get; } = [];
    public Dictionary<Guid, ImportRun> Runs { get; } = [];
    public List<RawRecord> RawRecords { get; } = [];
    public List<Transaction> Transactions { get; } = [];

    public Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(accountId == AccountId);

    public Task<IReadOnlyList<Asset>> GetAssetsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Asset>>([.. Assets]);

    public Task<ImportRun?> GetRunAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Runs.GetValueOrDefault(id));

    public Task AddRunAsync(ImportRun run, CancellationToken cancellationToken)
    {
        Runs[run.Id] = run;
        return Task.CompletedTask;
    }

    public Task DeleteRunAsync(ImportRun run, CancellationToken cancellationToken)
    {
        Runs.Remove(run.Id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlySet<string>> FindExistingDedupKeysAsync(
        string source, IReadOnlyCollection<RawRecord> candidates, CancellationToken cancellationToken)
    {
        var stored = RawRecords.Where(r => r.Source == source).Select(r => r.DedupKey).ToHashSet();
        return Task.FromResult<IReadOnlySet<string>>(
            candidates.Select(c => c.DedupKey).Where(stored.Contains).ToHashSet());
    }

    public Task<IReadOnlyList<(DateTimeOffset TimestampUtc, string Fingerprint)>> FindOtherSourceFingerprintsAsync(
        Guid accountId, string excludeSource, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(DateTimeOffset, string)>>(Transactions
            .Where(t => t.AccountId == accountId && t.Source != excludeSource && t.TimestampUtc >= from && t.TimestampUtc <= to)
            .Select(t => (t.TimestampUtc, t.Fingerprint()))
            .ToList());

    public Task CommitAsync(
        ImportRun run, IReadOnlyCollection<Asset> newAssets, IReadOnlyCollection<RawRecord> rawRecords,
        IReadOnlyCollection<Transaction> transactions, CancellationToken cancellationToken)
    {
        Assets.AddRange(newAssets);
        RawRecords.AddRange(rawRecords);
        Transactions.AddRange(transactions);
        Runs[run.Id] = run;
        return Task.CompletedTask;
    }
}
