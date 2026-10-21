using System.Text;
using KryptoSteuer.Application.Bitget;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Application.Secrets;
using KryptoSteuer.Domain.Ledger;

namespace KryptoSteuer.Importers.Bitget;

/// <summary>Erzeugt einen Bitget-Client für Zugangsdaten (in Infrastructure über <c>IHttpClientFactory</c>).</summary>
public interface IBitgetClientFactory
{
    IBitgetClient Create(BitgetCredentials credentials);
}

public sealed record BitgetSyncResult(
    ImportReport Import,
    IReadOnlyList<string> FailedSections,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<BalanceMismatch> BalanceMismatches,
    string? BalanceCheckSkipped);

public sealed record BitgetKeyCheck(string AccountType, IReadOnlyList<string> Warnings);

public sealed class BitgetSyncException(string message) : Exception(message);

/// <summary>
/// Zugangsdaten verwalten und synchronisieren. Ablauf je Sync: Rechte prüfen, seit dem letzten Erfolg abrufen (mit Überlappung),
/// das Rohdaten-Bündel durch das Import-Framework schicken und festschreiben, Sync-Zustand je Endpunkt nachziehen,
/// dann Salden mit dem berechneten Bestand abgleichen (Abweichungen in die Review-Queue).
/// </summary>
public sealed class BitgetSyncService(
    IBitgetSyncStore store,
    IImportStore importStore,
    ImportService imports,
    ISecretProtector secrets,
    IBitgetClientFactory clients,
    TimeProvider clock)
{
    public const string Provider = "bitget";

    /// <summary>Beim inkrementellen Sync wird so weit vor dem letzten Erfolg neu begonnen (ausstehende Ein-/Auszahlungen, späte Buchungen).</summary>
    public static readonly TimeSpan Overlap = TimeSpan.FromDays(7);

    /// <summary>Prüft den Key (Kontotyp, Rechte) und speichert ihn verschlüsselt. Ungültige Keys werden nicht gespeichert.</summary>
    public async Task<BitgetKeyCheck> SaveCredentialsAsync(
        Guid accountId, BitgetCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentials.ApiKey) || string.IsNullOrWhiteSpace(credentials.Secret)
            || string.IsNullOrWhiteSpace(credentials.Passphrase))
            throw new BitgetSyncException("API-Key, Secret und Passphrase sind Pflicht.");
        if (!await importStore.AccountExistsAsync(accountId, cancellationToken))
            throw new BitgetSyncException("Das Konto existiert nicht.");

        BitgetProbeResult probe;
        try
        {
            probe = await new BitgetFetcher(clients.Create(credentials), clock).ProbeAsync(cancellationToken);
        }
        catch (BitgetApiException ex)
        {
            // Die Meldung der Börse enthält keine Geheimnisse; Keys selbst werden nie ausgegeben.
            throw new BitgetSyncException($"Bitget hat den Key abgelehnt: {ex.Message}");
        }

        var accountType = probe.AccountType == BitgetAccountType.Unified ? BitgetBundle.UnifiedAccount : BitgetBundle.ClassicAccount;
        var existing = await store.GetCredentialAsync(accountId, cancellationToken);
        var credential = existing ?? new ExchangeCredential
        {
            AccountId = accountId, Provider = Provider, CreatedAtUtc = clock.GetUtcNow(),
            EncryptedApiKey = "", EncryptedSecret = "", EncryptedPassphrase = "",
        };
        credential.EncryptedApiKey = secrets.Protect(credentials.ApiKey);
        credential.EncryptedSecret = secrets.Protect(credentials.Secret);
        credential.EncryptedPassphrase = secrets.Protect(credentials.Passphrase);
        credential.AccountType = accountType;
        credential.PermissionWarnings = probe.Warnings.Count == 0 ? null : string.Join("\n", probe.Warnings);
        await store.SaveCredentialAsync(credential, cancellationToken);

        return new BitgetKeyCheck(accountType, probe.Warnings);
    }

    public async Task<BitgetSyncResult> SyncAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var credential = await store.GetCredentialAsync(accountId, cancellationToken)
            ?? throw new BitgetSyncException("Für dieses Konto ist kein Bitget-Key hinterlegt.");

        var client = clients.Create(new BitgetCredentials(
            secrets.Unprotect(credential.EncryptedApiKey),
            secrets.Unprotect(credential.EncryptedSecret),
            secrets.Unprotect(credential.EncryptedPassphrase)));
        var fetcher = new BitgetFetcher(client, clock);

        var to = clock.GetUtcNow();
        BitgetProbeResult probe;
        try
        {
            probe = await fetcher.ProbeAsync(cancellationToken);
        }
        catch (BitgetApiException ex)
        {
            await RecordAsync(credential, to, $"Fehlgeschlagen: {ex.Message}", cancellationToken);
            throw new BitgetSyncException($"Bitget-Zugriff fehlgeschlagen: {ex.Message}");
        }

        var states = await store.GetSyncStatesAsync(accountId, cancellationToken);
        var expected = BitgetFetcher.ExpectedSections(probe.AccountType);
        // Fehlt für einen Endpunkt der Zeitpunkt (z. B. weil er zuvor ausgefallen ist), beginnt der Abruf wieder bei 90 Tagen.
        var from = expected.All(states.ContainsKey)
            ? states.Where(kv => expected.Contains(kv.Key)).Min(kv => kv.Value) - Overlap
            : to.AddDays(-BitgetWindows.MaxHistoryDays);

        BitgetFetchResult fetched;
        try
        {
            fetched = await fetcher.FetchAsync(from, to, cancellationToken, probe);
        }
        catch (BitgetApiException ex)
        {
            await RecordAsync(credential, to, $"Fehlgeschlagen: {ex.Message}", cancellationToken);
            throw new BitgetSyncException($"Bitget-Abruf fehlgeschlagen: {ex.Message}");
        }

        var run = await imports.PreviewAsync(
            new ImportInput($"bitget-sync-{to:yyyyMMddTHHmmss}.json", Encoding.UTF8.GetBytes(fetched.BundleJson)),
            accountId, BitgetApiImporter.SourceName, cancellationToken);
        var committed = await imports.CommitAsync(run.Id, cancellationToken);

        // Zeitpunkt nur für Endpunkte nachziehen, die vollständig abgerufen wurden.
        var newStates = fetched.SucceededSections.ToDictionary(kind => kind, _ => to);
        await store.SetSyncStatesAsync(accountId, newStates, cancellationToken);

        var (mismatches, skipped) = await ReconcileAsync(client, probe.AccountType, accountId, to, cancellationToken);

        var summary = $"{committed.Report.New} neue Buchungen, {committed.Report.Duplicates} Duplikate, " +
                      $"{committed.Report.Errors.Count} Fehler, {fetched.FailedSections.Count} Endpunkte ausgefallen, " +
                      $"{mismatches.Count} Saldo-Abweichungen";
        credential.AccountType = probe.AccountType == BitgetAccountType.Unified ? BitgetBundle.UnifiedAccount : BitgetBundle.ClassicAccount;
        credential.PermissionWarnings = probe.Warnings.Count == 0 ? null : string.Join("\n", probe.Warnings);
        await RecordAsync(credential, to, summary, cancellationToken);

        return new BitgetSyncResult(committed.Report, fetched.FailedSections, probe.Warnings, mismatches, skipped);
    }

    private async Task<(IReadOnlyList<BalanceMismatch> Mismatches, string? Skipped)> ReconcileAsync(
        IBitgetClient client, BitgetAccountType type, Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, decimal> exchange;
        try
        {
            exchange = await BitgetBalances.FetchTotalsAsync(client, type, cancellationToken);
        }
        catch (BitgetApiException ex)
        {
            // Ohne vollständige Salden kein Abgleich, damit fehlende Wallets keine falschen Abweichungen erzeugen.
            return ([], $"Saldo-Abgleich übersprungen: {ex.Message}");
        }

        var holdings = await store.GetHoldingsAsync(accountId, cancellationToken);
        var mismatches = BalanceReconciler.Compare(exchange, holdings);
        await store.ApplyBalanceMismatchesAsync(accountId, mismatches, now, cancellationToken);
        return (mismatches, null);
    }

    private async Task RecordAsync(ExchangeCredential credential, DateTimeOffset at, string summary, CancellationToken cancellationToken)
    {
        credential.LastSyncAtUtc = at;
        credential.LastSyncSummary = summary;
        await store.SaveCredentialAsync(credential, cancellationToken);
    }
}
