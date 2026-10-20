using KryptoSteuer.Application.Accounts;
using KryptoSteuer.Application.Health;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Application.Secrets;
using KryptoSteuer.Infrastructure.Secrets;
using KryptoSteuer.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KryptoSteuer.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Postgres";
    public const string DataProtectionKeysPathKey = "DataProtection:KeysPath";

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
        services.AddScoped<EfImportStore>();
        services.AddScoped<IImportStore>(sp => sp.GetRequiredService<EfImportStore>());
        services.AddScoped<IAccountRepository>(sp => sp.GetRequiredService<EfImportStore>());

        // Schlüsselring außerhalb der Datenbank; ohne Pfad gilt der Standardort des Betriebssystems
        // (lokal: Benutzerprofil, im Container: Volume über DataProtection__KeysPath).
        services.AddDataProtection().SetApplicationName("KryptoSteuer");
        services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, ILoggerFactory>(
            (options, configuration, loggerFactory) =>
            {
                var keysPath = configuration[DataProtectionKeysPathKey];
                if (!string.IsNullOrWhiteSpace(keysPath))
                    options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keysPath), loggerFactory);
            });
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        return services;
    }
}
