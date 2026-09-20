using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace LandErp.Infrastructure.Modules.Collection;

public static class CollectionServices
{
    public static IServiceCollection AddLandErpCollection(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICollectionAdministration, CollectionAdministration>();
        services.AddScoped<ICollectorGateway, CollectorGateway>();
        services.AddScoped<ICollectionScheduler, CollectionScheduler>();
        services.AddScoped<IDuplicateDetectionSettingsService, DuplicateDetectionSettingsService>();
        services.AddScoped<IIncomingDuplicateMatchingMaintenance, IncomingDuplicateMatchingMaintenance>();
        return services;
    }
}
