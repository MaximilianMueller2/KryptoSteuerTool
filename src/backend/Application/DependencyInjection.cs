using KryptoSteuer.Application.Accounts;
using KryptoSteuer.Application.Health;
using KryptoSteuer.Application.Imports;
using Microsoft.Extensions.DependencyInjection;

namespace KryptoSteuer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GetHealthUseCase>();
        services.AddScoped<ImportService>();
        services.AddScoped<AccountUseCases>();
        return services;
    }
}
