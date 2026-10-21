using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Application.Bitget;

/// <summary>Port: Persistenz für Börsen-Zugangsdaten, Sync-Zustand und Review-Einträge.</summary>
public interface IBitgetSyncStore
{
    Task<ExchangeCredential?> GetCredentialAsync(Guid accountId, CancellationToken cancellationToken);

    Task SaveCredentialAsync(ExchangeCredential credential, CancellationToken cancellationToken);

    Task<bool> DeleteCredentialAsync(Guid accountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> AccountsWithCredentialsAsync(string provider, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, DateTimeOffset>> GetSyncStatesAsync(Guid accountId, CancellationToken cancellationToken);

    Task SetSyncStatesAsync(Guid accountId, IReadOnlyDictionary<string, DateTimeOffset> states, CancellationToken cancellationToken);

    /// <summary>Bestand je Symbol aus den Buchungen des Kontos (In minus Out, ohne Ignored).</summary>
    Task<IReadOnlyDictionary<string, decimal>> GetHoldingsAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Gleicht offene Saldo-Abweichungen des Kontos ab: legt Einträge für neue Abweichungen an, aktualisiert bestehende
    /// und schließt Einträge für Symbole, die nicht mehr abweichen.
    /// </summary>
    Task ApplyBalanceMismatchesAsync(
        Guid accountId, IReadOnlyList<BalanceMismatch> mismatches, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewItem>> ListOpenReviewItemsAsync(CancellationToken cancellationToken);
}

/// <summary>Abweichung zwischen Börsen-Saldo und aus den Buchungen berechnetem Bestand.</summary>
public sealed record BalanceMismatch(string Symbol, decimal ExchangeBalance, decimal ComputedHolding)
{
    public decimal Difference => ExchangeBalance - ComputedHolding;
}
