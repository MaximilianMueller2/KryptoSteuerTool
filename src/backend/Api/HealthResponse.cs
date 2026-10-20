using System.Reflection;

namespace KryptoSteuer.Api;

/// <param name="Status">"Healthy" oder "Unhealthy".</param>
/// <param name="Database">"Up" oder "Down".</param>
/// <param name="Version">Build-Version der API.</param>
/// <param name="TimeUtc">Serverzeit in UTC.</param>
public sealed record HealthResponse(string Status, string Database, string Version, DateTimeOffset TimeUtc);

public static class ApiInfo
{
    public static string Version { get; } =
        typeof(ApiInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}
