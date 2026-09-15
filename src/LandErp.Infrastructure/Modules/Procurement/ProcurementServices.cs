using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Catalog.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace LandErp.Infrastructure.Modules.Procurement;

public static class ProcurementServices
{
    public static IServiceCollection AddLandErpProcurement(this IServiceCollection services)
    {
        services.AddScoped<ProcurementWorkspace>();
        services.AddScoped<IProcurementWorkspace>(provider => provider.GetRequiredService<ProcurementWorkspace>());
        services.AddScoped<ICatalogWorkspace>(provider => provider.GetRequiredService<ProcurementWorkspace>());
        return services;
    }
}
