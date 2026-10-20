using KryptoSteuer.Application.Bitget;
using KryptoSteuer.Importers.Bitget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KryptoSteuer.Api.Bitget;

/// <summary>
/// Hintergrundjob: synchronisiert täglich (Standard 03:30 UTC) alle Konten mit Bitget-Key.
/// Eigener Hosted Service statt Hangfire/Quartz, weil ein einziger täglicher Lauf für eine Person reicht.
/// Abschaltbar über <c>Bitget:DailySync:Enabled=false</c>.
/// </summary>
public sealed class BitgetDailySyncService(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<BitgetDailySyncService> logger, TimeSpan? runAtUtc = null) : BackgroundService
{
    private readonly TimeSpan _runAt = runAtUtc ?? new TimeSpan(3, 30, 0);

    public static DateTimeOffset NextRun(DateTimeOffset now, TimeSpan runAtUtc)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero) + runAtUtc;
        return today > now ? today : today.AddDays(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = NextRun(clock.GetUtcNow(), _runAt) - clock.GetUtcNow();
            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IBitgetSyncStore>();
        var accounts = await store.AccountsWithCredentialsAsync(BitgetSyncService.Provider, cancellationToken);

        foreach (var target in accounts)
        {
            try
            {
                // Eigener Scope je Konto: ein Fehler (oder ein verworfener DbContext) betrifft nur dieses Konto.
                await using var accountScope = scopes.CreateAsyncScope();
                var result = await accountScope.ServiceProvider.GetRequiredService<BitgetSyncService>()
                    .SyncAsync(target, cancellationToken);
                logger.LogInformation("Bitget-Sync für Ziel {Target}: {New} neue Buchungen, {Mismatches} Abweichungen.",
                    target, result.Import.New, result.BalanceMismatches.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bewusst ohne Zugangsdaten im Log; geloggt wird nur die interne ID des Sync-Ziels.
                logger.LogWarning("Bitget-Sync für Ziel {Target} fehlgeschlagen: {Message}", target, ex.Message);
            }
        }
    }
}
