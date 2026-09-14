using LandErp.Application.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace LandErp.Server.Security;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionAuthorization(IAccessControl access) : AuthorizationHandler<PermissionRequirement>
{
    public static Subject SubjectFrom(ClaimsPrincipal principal) =>
        new(Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : Guid.Empty,
            principal.HasClaim("amr", "mfa"));

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        try
        {
            await access.RequireAsync(SubjectFrom(context.User), requirement.Permission, CancellationToken.None);
            context.Succeed(requirement);
        }
        catch (AccessDeniedException)
        {
            // Deny by default, including a valid cookie without the requested current DB grant.
        }
    }
}
