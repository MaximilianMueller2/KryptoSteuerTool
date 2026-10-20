using KryptoSteuer.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace KryptoSteuer.Infrastructure.Persistence;

/// <summary>
/// EF-Core-Kontext des Ledgers. Mengen sind decimal(38,18), EUR-Beträge decimal(28,8);
/// gerundet wird erst im Report.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<RawRecord> RawRecords => Set<RawRecord>();
    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();
    public DbSet<ExchangeCredential> ExchangeCredentials => Set<ExchangeCredential>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();
    public DbSet<ReviewItem> ReviewItems => Set<ReviewItem>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionLeg> TransactionLegs => Set<TransactionLeg>();
    public DbSet<TransferLink> TransferLinks => Set<TransferLink>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<Disposal> Disposals => Set<Disposal>();
    public DbSet<TaxRun> TaxRuns => Set<TaxRun>();
    public DbSet<ReviewDecision> ReviewDecisions => Set<ReviewDecision>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RawRecordGuard.EnsureUntouched(ChangeTracker);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RawRecordGuard.EnsureUntouched(ChangeTracker);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
