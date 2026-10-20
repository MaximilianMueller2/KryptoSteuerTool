using KryptoSteuer.Application.Health;
using KryptoSteuer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KryptoSteuer.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Postgres";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Der Connection-String wird erst beim ersten Zugriff gelesen. So greifen auch
        // Konfigurationsüberschreibungen, die später hinzukommen (z. B. in Integrationstests).
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"ConnectionStrings:{ConnectionStringName} ist nicht konfiguriert.");
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IDatabaseProbe, NpgsqlDatabaseProbe>();
        return services;
    }
}
