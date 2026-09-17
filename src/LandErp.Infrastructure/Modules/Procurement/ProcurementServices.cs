using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Infrastructure.Foundation.Files;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Infrastructure.Modules.Overview;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LandErp.Infrastructure.Modules.Procurement;

public static class ProcurementServices
{
    public static IServiceCollection AddLandErpProcurement(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddLandErpFileStorage(configuration, environment);
        services.AddScoped<ProcurementWorkspace>();
        services.AddScoped<IProcurementWorkspace>(provider => provider.GetRequiredService<ProcurementWorkspace>());
        services.AddScoped<ICatalogWorkspace>(provider => provider.GetRequiredService<ProcurementWorkspace>());
        services.AddScoped<IIncomingCatalogReadService, IncomingCatalogReadService>();
        services.AddScoped<IIncomingFilterPresetService, IncomingFilterPresetService>();
        services.AddScoped<IProcurementQueueV2ReadService, ProcurementQueueV2ReadService>();
        services.AddScoped<IOverviewService, OverviewService>();
        return services;
    }
}
