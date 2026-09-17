using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.IdentityAccess;

public sealed class AccessControl(IDbContextFactory<LandErpDbContext> factory) : IAccessControl
{
    public async Task<AccessContext> ResolveAsync(Subject subject, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var assignment = await (from employee in db.Employees
                                join value in db.EmployeeAssignments on employee.Id equals value.EmployeeId
                                where employee.UserId == subject.UserId && employee.Active
                                select new { employee.Id, employee.OrganizationId, value.OrgUnitId, value.TeamId, value.Scope })
            .SingleOrDefaultAsync(cancellationToken);
        return assignment == null ? throw new AccessDeniedException()
            : new(assignment.Id, assignment.OrganizationId, assignment.OrgUnitId, assignment.TeamId, assignment.Scope);
    }

    public async Task<AccessContext> RequireAsync(Subject subject, string permission, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var grants = await (from employee in db.Employees
                            join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
                            join role in db.Roles on assignment.RoleId equals role.Id
                            join grant in db.RolePermissions on role.Id equals grant.RoleId
                            join user in db.Users on employee.UserId equals user.Id
                            where employee.UserId == subject.UserId && employee.Active && grant.PermissionId == permission
                            select new { employee.Id, employee.OrganizationId, assignment.OrgUnitId,
                                assignment.TeamId, assignment.Scope, role.Name, user.TwoFactorEnabled }).ToListAsync(cancellationToken);
        var allowed = grants.Where(grant => grant.Name is not ("Owner" or "Administrator")
            || (subject.MultiFactorAuthenticated && grant.TwoFactorEnabled))
            .OrderByDescending(grant => grant.Scope).FirstOrDefault();
        return allowed == null ? throw new AccessDeniedException()
            : new(allowed.Id, allowed.OrganizationId, allowed.OrgUnitId, allowed.TeamId, allowed.Scope);
    }
}
