using KryptoSteuer.Application.Accounts;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace KryptoSteuer.Infrastructure.Persistence;

public sealed class EfImportStore(AppDbContext db) : IImportStore, IAccountRepository
{
    public Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(a => a.Id == accountId, cancellationToken);

    public async Task<IReadOnlyList<Asset>> GetAssetsAsync(CancellationToken cancellationToken) =>
        await db.Assets.AsNoTracking().ToListAsync(cancellationToken);

    public Task<ImportRun?> GetRunAsync(Guid id, CancellationToken cancellationToken) =>
        db.ImportRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task AddRunAsync(ImportRun run, CancellationToken cancellationToken)
    {
        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteRunAsync(ImportRun run, CancellationToken cancellationToken)
    {
        db.ImportRuns.Remove(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> FindExistingDedupKeysAsync(
        string source, IReadOnlyCollection<RawRecord> candidates, CancellationToken cancellationToken)
    {
        var externalIds = candidates.Where(c => c.ExternalId != null).Select(c => c.ExternalId!).Distinct().ToList();
        var hashes = candidates.Where(c => c.ExternalId == null).Select(c => c.ContentHash).Distinct().ToList();

        var existing = await db.RawRecords.AsNoTracking()
            .Where(r => r.Source == source
                && ((r.ExternalId != null && externalIds.Contains(r.ExternalId))
                    || (r.ExternalId == null && hashes.Contains(r.ContentHash))))
            .Select(r => new { r.ExternalId, r.ContentHash })
            .ToListAsync(cancellationToken);

        return existing
            .Select(r => IdempotencyKey.FromHash(source, r.ExternalId, r.ContentHash))
            .ToHashSet();
    }

    public async Task CommitAsync(
        ImportRun run,
        IReadOnlyCollection<Asset> newAssets,
        IReadOnlyCollection<RawRecord> rawRecords,
        IReadOnlyCollection<Transaction> transactions,
        CancellationToken cancellationToken)
    {
        // Eine Transaktion: entweder wird alles festgeschrieben oder nichts.
        db.Assets.AddRange(newAssets);
        db.RawRecords.AddRange(rawRecords);
        db.Transactions.AddRange(transactions);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(Account account, CancellationToken cancellationToken)
    {
        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync(cancellationToken);
}
