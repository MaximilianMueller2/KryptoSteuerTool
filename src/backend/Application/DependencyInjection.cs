using KryptoSteuer.Application.Accounts;
using KryptoSteuer.Application.Health;
using KryptoSteuer.Application.Imports;
using KryptoSteuer.Application.Prices;
using KryptoSteuer.Application.Tax;
using Microsoft.Extensions.DependencyInjection;

namespace KryptoSteuer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GetHealthUseCase>();
        services.AddScoped<ImportService>();
        services.AddScoped<AccountUseCases>();
        services.AddScoped<PriceService>();
        services.AddScoped<TaxRunService>();
        services.AddScoped<ReviewService>();
        return services;
    }
}
