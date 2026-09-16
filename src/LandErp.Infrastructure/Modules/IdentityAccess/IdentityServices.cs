using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LandErp.Infrastructure.Modules.IdentityAccess;

public static class IdentityServices
{
    public static IServiceCollection AddLandErpIdentity(this IServiceCollection services)
    {
        services.AddIdentity<LandErpUser, IdentityRole<Guid>>(options =>
        {
            options.Password.RequiredLength = 12;
            options.User.RequireUniqueEmail = false;
            options.SignIn.RequireConfirmedEmail = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<LandErpDbContext>().AddDefaultTokenProviders();
        services.AddScoped<IAccessControl, AccessControl>();
        services.AddScoped<IOrganizationWorkspace, OrganizationWorkspace>();
        return services;
    }
}
