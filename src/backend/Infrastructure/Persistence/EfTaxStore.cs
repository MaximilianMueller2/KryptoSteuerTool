using KryptoSteuer.Application.Prices;
using KryptoSteuer.Application.Tax;
using KryptoSteuer.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace KryptoSteuer.Infrastructure.Persistence;

public sealed class EfTaxStore(AppDbContext db) : ITaxDataStore, IPriceStore, IReviewStore
{
    public async Task<IReadOnlyList<Transaction>> GetTransactionsAsync(CancellationToken cancellationToken) =>
        await db.Transactions.AsNoTracking().Include(t => t.Legs).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Asset>> GetAssetsAsync(CancellationToken cancellationToken) =>
        await db.Assets.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Price>> GetPricesAsync(CancellationToken cancellationToken) =>
        await db.Prices.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReviewDecision>> GetDecisionsAsync(CancellationToken cancellationToken) =>
        await db.ReviewDecisions.AsNoTracking().OrderBy(d => d.CreatedAtUtc).ToListAsync(cancellationToken);

    public Task<Transaction?> GetTransactionAsync(Guid id, CancellationToken cancellationToken) =>
        db.Transactions.AsNoTracking().Include(t => t.Legs).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<ReviewDecision?> GetDecisionAsync(Guid transactionId, CancellationToken cancellationToken) =>
        db.ReviewDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.TransactionId == transactionId, cancellationToken);

    public async Task SaveAsync(ReviewDecision decision, AuditLogEntry audit, CancellationToken cancellationToken)
    {
        // Eine Entscheidung je Buchung: eine vorhandene wird ersetzt (init-only Felder), beides in einer Transaktion samt Audit-Eintrag.
        await using var dbTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.ReviewDecisions.Where(d => d.TransactionId == decision.TransactionId).ExecuteDeleteAsync(cancellationToken);
        db.ReviewDecisions.Add(decision);
        db.AuditLog.Add(audit);
        await db.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveAsync(ReviewDecision decision, AuditLogEntry audit, CancellationToken cancellationToken)
    {
        db.AuditLog.Add(audit);
        db.ReviewDecisions.Remove(decision);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRunAsync(TaxRun run, CancellationToken cancellationToken)
    {
        db.TaxRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<TaxRun?> GetLatestRunAsync(int year, CancellationToken cancellationToken) =>
        db.TaxRuns.AsNoTracking().Where(r => r.TaxYear == year).OrderByDescending(r => r.StartedAtUtc).FirstOrDefaultAsync(cancellationToken);

    public Task<TaxRun?> GetRunAsync(Guid id, CancellationToken cancellationToken) =>
        db.TaxRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<int> UpsertAsync(IReadOnlyCollection<Price> prices, CancellationToken cancellationToken)
    {
        if (prices.Count == 0) return 0;

        var assetIds = prices.Select(p => p.AssetId).Distinct().ToList();
        var existing = await db.Prices.Where(p => assetIds.Contains(p.AssetId)).ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(p => (p.AssetId, p.TimestampUtc, p.Source));

        var changed = 0;
        foreach (var p in prices)
        {
            if (byKey.TryGetValue((p.AssetId, p.TimestampUtc, p.Source), out var current))
            {
                if (current.EurPrice == p.EurPrice) continue;
                // Price.EurPrice ist init-only: Eintrag ersetzen.
                db.Prices.Remove(current);
            }

            db.Prices.Add(new Price { AssetId = p.AssetId, TimestampUtc = p.TimestampUtc, EurPrice = p.EurPrice, Source = p.Source });
            changed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    public async Task<IReadOnlyList<Price>> ListAsync(Guid? assetId, CancellationToken cancellationToken) =>
        await db.Prices.AsNoTracking().Where(p => assetId == null || p.AssetId == assetId)
            .OrderBy(p => p.AssetId).ThenBy(p => p.TimestampUtc).Take(5000).ToListAsync(cancellationToken);
}
