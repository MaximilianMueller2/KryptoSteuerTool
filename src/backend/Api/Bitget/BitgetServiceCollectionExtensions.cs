using KryptoSteuer.Api.Prices;
using KryptoSteuer.Importers.Bitget;

namespace KryptoSteuer.Api.Bitget;

public static class BitgetServiceCollectionExtensions
{
    /// <summary>Bitget-Sync: HTTP-Client, Client-Fabrik, Sync-Dienst und täglicher Hintergrundjob (abschaltbar).</summary>
    public static IServiceCollection AddBitgetSync(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(BitgetClientFactory.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.bitget.com");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IBitgetClientFactory, BitgetClientFactory>();
        services.AddScoped<BitgetSyncService>();

        // EZB-Referenzkurse für die Umrechnung von USD-Stablecoins
        services.AddHttpClient(EcbRatesClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddScoped(sp => new EcbRatesClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(EcbRatesClient.HttpClientName), configuration));

        if (configuration.GetValue("Bitget:DailySync:Enabled", true))
            services.AddHostedService(sp => ActivatorUtilities.CreateInstance<BitgetDailySyncService>(sp));

        return services;
    }
}
