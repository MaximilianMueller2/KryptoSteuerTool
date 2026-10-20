using KryptoSteuer.Application.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryptoSteuer.Infrastructure.Persistence;

public sealed class NpgsqlDatabaseProbe(AppDbContext db, ILogger<NpgsqlDatabaseProbe> logger) : IDatabaseProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            return await db.Database.CanConnectAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Datenbankprüfung nach {Timeout} abgebrochen.", Timeout);
            return false;
        }
        catch (Exception ex)
        {
            // Bewusst ohne Connection-String im Log (enthält das Passwort).
            logger.LogWarning(ex, "Datenbank nicht erreichbar.");
            return false;
        }
    }
}
