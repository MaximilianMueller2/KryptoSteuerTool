using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Application.Tax;

/// <summary>Port: liest die Eingabedaten der Steuer-Engine und speichert Berechnungsläufe.</summary>
public interface ITaxDataStore
{
    /// <summary>Alle Buchungen mit Beinen (auch Ignored und NeedsReview; die Engine entscheidet).</summary>
    Task<IReadOnlyList<Transaction>> GetTransactionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Asset>> GetAssetsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Price>> GetPricesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewDecision>> GetDecisionsAsync(CancellationToken cancellationToken);

    Task AddRunAsync(TaxRun run, CancellationToken cancellationToken);

    Task<TaxRun?> GetLatestRunAsync(int year, CancellationToken cancellationToken);

    Task<TaxRun?> GetRunAsync(Guid id, CancellationToken cancellationToken);
}
