using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Application.Imports;

/// <summary>Port: Persistenz für Importläufe. Implementiert in Infrastructure.</summary>
public interface IImportStore
{
    Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Asset>> GetAssetsAsync(CancellationToken cancellationToken);

    Task<ImportRun?> GetRunAsync(Guid id, CancellationToken cancellationToken);

    Task AddRunAsync(ImportRun run, CancellationToken cancellationToken);

    Task DeleteRunAsync(ImportRun run, CancellationToken cancellationToken);

    /// <summary>Liefert die Dedup-Schlüssel (siehe <see cref="RawRecord.DedupKey"/>) der Kandidaten, die schon gespeichert sind.</summary>
    Task<IReadOnlySet<string>> FindExistingDedupKeysAsync(
        string source, IReadOnlyCollection<RawRecord> candidates, CancellationToken cancellationToken);

    /// <summary>Schreibt Assets, Rohdaten, Buchungen und den Lauf in einer Transaktion.</summary>
    Task CommitAsync(
        ImportRun run,
        IReadOnlyCollection<Asset> newAssets,
        IReadOnlyCollection<RawRecord> rawRecords,
        IReadOnlyCollection<Transaction> transactions,
        CancellationToken cancellationToken);
}
