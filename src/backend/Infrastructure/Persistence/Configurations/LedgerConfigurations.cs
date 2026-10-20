using KryptoSteuer.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KryptoSteuer.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Platform).HasMaxLength(100).IsRequired();
        b.Property(x => x.Country).HasMaxLength(2);
        b.Property(x => x.SubAccount).HasMaxLength(100);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Symbol).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Isin).HasMaxLength(12);
        b.Property(x => x.ContractAddress).HasMaxLength(200);
        b.Property(x => x.Chain).HasMaxLength(50);
        b.Property(x => x.Class).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.Symbol);
    }
}

internal sealed class RawRecordConfiguration : IEntityTypeConfiguration<RawRecord>
{
    public void Configure(EntityTypeBuilder<RawRecord> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Source).HasMaxLength(50).IsRequired();
        b.Property(x => x.ExternalId).HasMaxLength(200);
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        b.Ignore(x => x.DedupKey);
        b.HasIndex(x => x.ImportRunId);
        // Idempotenz: (Quelle, externe ID) eindeutig, solange es eine externe ID gibt.
        b.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique().HasFilter("\"ExternalId\" IS NOT NULL");
        b.HasIndex(x => new { x.Source, x.ContentHash }).IsUnique().HasFilter("\"ExternalId\" IS NULL");
    }
}

internal sealed class ImportRunConfiguration : IEntityTypeConfiguration<ImportRun>
{
    public void Configure(EntityTypeBuilder<ImportRun> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Source).HasMaxLength(50).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Content).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ReportJson).HasColumnType("jsonb").IsRequired();
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.FileHash);
    }
}

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Source).HasMaxLength(50).IsRequired();
        b.Property(x => x.ExternalId).HasMaxLength(200);
        b.Property(x => x.ContentHash).HasMaxLength(64);
        b.Property(x => x.RawRecordIds).HasColumnType("uuid[]");
        b.Ignore(x => x.DedupKey);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Legs).WithOne().HasForeignKey(l => l.TransactionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.AccountId, x.TimestampUtc });
        b.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique().HasFilter("\"ExternalId\" IS NOT NULL");
        b.HasIndex(x => new { x.Source, x.ContentHash }).IsUnique()
            .HasFilter("\"ExternalId\" IS NULL AND \"ContentHash\" IS NOT NULL");
    }
}

internal sealed class TransactionLegConfiguration : IEntityTypeConfiguration<TransactionLeg>
{
    public void Configure(EntityTypeBuilder<TransactionLeg> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(38, 18);
        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_transaction_leg_amount_positive", "\"Amount\" > 0"));
    }
}

internal sealed class TransferLinkConfiguration : IEntityTypeConfiguration<TransferLink>
{
    public void Configure(EntityTypeBuilder<TransferLink> b)
    {
        b.HasKey(x => x.Id);
        b.HasOne<Transaction>().WithMany().HasForeignKey(x => x.TransferOutTransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Transaction>().WithMany().HasForeignKey(x => x.TransferInTransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.TransferOutTransactionId).IsUnique();
        b.HasIndex(x => x.TransferInTransactionId).IsUnique();
    }
}

internal sealed class PriceConfiguration : IEntityTypeConfiguration<Price>
{
    public void Configure(EntityTypeBuilder<Price> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.EurPrice).HasPrecision(28, 8);
        b.Property(x => x.Source).HasMaxLength(100).IsRequired();
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AssetId, x.TimestampUtc, x.Source }).IsUnique();
    }
}

internal sealed class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(38, 18);
        b.Property(x => x.RemainingAmount).HasPrecision(38, 18);
        b.Property(x => x.CostEur).HasPrecision(28, 8);
        b.Property(x => x.Regime).HasConversion<string>().HasMaxLength(10);
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Transaction>().WithMany().HasForeignKey(x => x.SourceTransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AccountId, x.AssetId, x.AcquiredAtUtc });
    }
}

internal sealed class DisposalConfiguration : IEntityTypeConfiguration<Disposal>
{
    public void Configure(EntityTypeBuilder<Disposal> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(38, 18);
        b.Property(x => x.ProceedsEur).HasPrecision(28, 8);
        b.Property(x => x.CostEur).HasPrecision(28, 8);
        b.Property(x => x.FeeShareEur).HasPrecision(28, 8);
        b.Property(x => x.GainEur).HasPrecision(28, 8);
        b.Property(x => x.RuleSetVersion).HasMaxLength(50).IsRequired();
        b.HasOne<TaxRun>().WithMany().HasForeignKey(x => x.TaxRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Lot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Transaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaxRunConfiguration : IEntityTypeConfiguration<TaxRun>
{
    public void Configure(EntityTypeBuilder<TaxRun> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.RuleSetVersion).HasMaxLength(50).IsRequired();
        b.Property(x => x.ParametersJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.InputHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ResultSnapshotJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.TaxYear, x.InputHash });
    }
}

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable("AuditLog");
        b.HasKey(x => x.Id);
        b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        b.Property(x => x.BeforeJson).HasColumnType("jsonb");
        b.Property(x => x.AfterJson).HasColumnType("jsonb");
        b.Property(x => x.Reason).IsRequired();
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

internal sealed class ExchangeCredentialConfiguration : IEntityTypeConfiguration<ExchangeCredential>
{
    public void Configure(EntityTypeBuilder<ExchangeCredential> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        b.Property(x => x.EncryptedApiKey).IsRequired();
        b.Property(x => x.EncryptedSecret).IsRequired();
        b.Property(x => x.EncryptedPassphrase).IsRequired();
        b.Property(x => x.AccountType).HasMaxLength(20);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.AccountId, x.Provider }).IsUnique();
    }
}

internal sealed class SyncStateConfiguration : IEntityTypeConfiguration<SyncState>
{
    public void Configure(EntityTypeBuilder<SyncState> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Endpoint).HasMaxLength(50).IsRequired();
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.AccountId, x.Endpoint }).IsUnique();
    }
}

internal sealed class ReviewItemConfiguration : IEntityTypeConfiguration<ReviewItem>
{
    public void Configure(EntityTypeBuilder<ReviewItem> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).IsRequired();
        b.Property(x => x.DetailsJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.Status, x.Kind });
        b.HasIndex(x => new { x.AccountId, x.AssetId });
    }
}
