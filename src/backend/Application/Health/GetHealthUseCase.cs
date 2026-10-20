namespace KryptoSteuer.Application.Health;

/// <summary>Port: prüft, ob die Datenbank erreichbar ist. Implementiert in Infrastructure.</summary>
public interface IDatabaseProbe
{
    /// <summary>Gibt <c>false</c> zurück, wenn die Datenbank nicht erreichbar ist; wirft nicht bei Verbindungsfehlern.</summary>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}

public sealed record HealthReport(bool DatabaseReachable, string Version, DateTimeOffset TimeUtc)
{
    public bool IsHealthy => DatabaseReachable;
    public string Status => IsHealthy ? "Healthy" : "Unhealthy";
}

public sealed class GetHealthUseCase(IDatabaseProbe databaseProbe, TimeProvider clock)
{
    public async Task<HealthReport> ExecuteAsync(string version, CancellationToken cancellationToken)
    {
        var reachable = await databaseProbe.CanConnectAsync(cancellationToken);
        return new HealthReport(reachable, version, clock.GetUtcNow());
    }
}
