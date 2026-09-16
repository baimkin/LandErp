using LandErp.Infrastructure.Modules.IdentityAccess;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Server.Security;

internal sealed class RevalidatingIdentityState(ILoggerFactory loggerFactory, IServiceScopeFactory scopes,
    IOptions<IdentityOptions> options) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        LandErpUser? user = await users.GetUserAsync(state.User);
        if (user == null || user.SecurityStamp != state.User.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType)) return false;
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        return await db.Employees.AnyAsync(item => item.UserId == user.Id && item.Active, cancellationToken);
    }
}
