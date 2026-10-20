using KryptoSteuer.Application.Health;
using Microsoft.Extensions.DependencyInjection;

namespace KryptoSteuer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GetHealthUseCase>();
        return services;
    }
}
