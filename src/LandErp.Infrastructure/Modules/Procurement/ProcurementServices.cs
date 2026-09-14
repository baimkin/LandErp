using LandErp.Application.Modules.Procurement.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace LandErp.Infrastructure.Modules.Procurement;

public static class ProcurementServices
{
    public static IServiceCollection AddLandErpProcurement(this IServiceCollection services)
    { services.AddScoped<IProcurementWorkspace, ProcurementWorkspace>(); return services; }
}
