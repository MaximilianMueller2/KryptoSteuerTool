using System.Globalization;
using System.Text.Json;
using KryptoSteuer.Application.Bitget;
using KryptoSteuer.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace KryptoSteuer.Infrastructure.Persistence;

public sealed class EfBitgetSyncStore(AppDbContext db) : IBitgetSyncStore
{
    public Task<ExchangeCredential?> GetCredentialAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.ExchangeCredentials.FirstOrDefaultAsync(c => c.AccountId == accountId && c.Provider == "bitget", cancellationToken);

    public async Task SaveCredentialAsync(ExchangeCredential credential, CancellationToken cancellationToken)
    {
        if (db.Entry(credential).State == EntityState.Detached)
            db.ExchangeCredentials.Add(credential);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteCredentialAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var credential = await GetCredentialAsync(accountId, cancellationToken);
        if (credential is null) return false;
        db.ExchangeCredentials.Remove(credential);
        db.SyncStates.RemoveRange(db.SyncStates.Where(s => s.AccountId == accountId));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<Guid>> AccountsWithCredentialsAsync(string provider, CancellationToken cancellationToken) =>
        await db.ExchangeCredentials.AsNoTracking().Where(c => c.Provider == provider).Select(c => c.AccountId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetSyncStatesAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.SyncStates.AsNoTracking().Where(s => s.AccountId == accountId)
            .ToDictionaryAsync(s => s.Endpoint, s => s.LastSuccessfulUntilUtc, cancellationToken);

    public async Task SetSyncStatesAsync(
        Guid accountId, IReadOnlyDictionary<string, DateTimeOffset> states, CancellationToken cancellationToken)
    {
        var existing = await db.SyncStates.Where(s => s.AccountId == accountId).ToListAsync(cancellationToken);
        foreach (var (endpoint, until) in states)
        {
            var state = existing.FirstOrDefault(s => s.Endpoint == endpoint);
            if (state is null)
                db.SyncStates.Add(new SyncState { AccountId = accountId, Endpoint = endpoint, LastSuccessfulUntilUtc = until });
            else if (until > state.LastSuccessfulUntilUtc)
                state.LastSuccessfulUntilUtc = until;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetHoldingsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var rows = await (
                from tx in db.Transactions
                where tx.AccountId == accountId && tx.Status != TransactionStatus.Ignored
                from leg in tx.Legs
                join asset in db.Assets on leg.AssetId equals asset.Id
                select new { asset.Symbol, leg.Direction, leg.Amount })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(r => r.Direction == LegDirection.In ? r.Amount : -r.Amount),
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task ApplyBalanceMismatchesAsync(
        Guid accountId, IReadOnlyList<BalanceMismatch> mismatches, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var assets = await db.Assets.AsNoTracking().ToListAsync(cancellationToken);
        var open = await db.ReviewItems
            .Where(r => r.AccountId == accountId && r.Kind == ReviewItemKind.BalanceMismatch && r.Status == ReviewItemStatus.Open)
            .ToListAsync(cancellationToken);

        var stillMismatching = new HashSet<Guid?>();
        foreach (var m in mismatches)
        {
            var asset = assets.FirstOrDefault(a => string.Equals(a.Symbol, m.Symbol, StringComparison.OrdinalIgnoreCase));
            var assetId = asset?.Id;
            stillMismatching.Add(assetId);

            var reason = $"Saldo-Abweichung {m.Symbol}: Bitget {Fmt(m.ExchangeBalance)}, berechnet {Fmt(m.ComputedHolding)}, " +
                         $"Differenz {Fmt(m.Difference)}.";
            var details = JsonSerializer.Serialize(new
            {
                symbol = m.Symbol, exchangeBalance = m.ExchangeBalance, computedHolding = m.ComputedHolding, difference = m.Difference,
            });

            var item = open.FirstOrDefault(r => r.AssetId == assetId);
            if (item is null)
                db.ReviewItems.Add(new ReviewItem
                {
                    Kind = ReviewItemKind.BalanceMismatch, Reason = reason, AccountId = accountId, AssetId = assetId,
                    DetailsJson = details, CreatedAtUtc = nowUtc,
                });
            else
            {
                item.Reason = reason;
                item.DetailsJson = details;
            }
        }

        // Nicht mehr abweichende Symbole: Eintrag schließen.
        foreach (var item in open.Where(r => !stillMismatching.Contains(r.AssetId)))
            item.Status = ReviewItemStatus.Resolved;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewItem>> ListOpenReviewItemsAsync(CancellationToken cancellationToken) =>
        await db.ReviewItems.AsNoTracking().Where(r => r.Status == ReviewItemStatus.Open)
            .OrderByDescending(r => r.CreatedAtUtc).ToListAsync(cancellationToken);

    private static string Fmt(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
