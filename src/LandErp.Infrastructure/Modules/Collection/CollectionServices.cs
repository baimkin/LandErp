using LandErp.Application.Modules.Collection.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace LandErp.Infrastructure.Modules.Collection;

public static class CollectionServices
{
    public static IServiceCollection AddLandErpCollection(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICollectionAdministration, CollectionAdministration>();
        services.AddScoped<ICollectorGateway, CollectorGateway>();
        return services;
    }
}
