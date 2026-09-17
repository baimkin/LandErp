using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;

namespace LandErp.Infrastructure.Modules.Procurement;

internal static class ProcurementVisibility
{
    public static IQueryable<PropertyCase> Apply(IQueryable<PropertyCase> cases, LandErpDbContext db, AccessContext context)
    {
        IQueryable<PropertyCase> organizationCases = cases.Where(item => item.OrganizationId == context.OrganizationId);
        return context.Scope switch
        {
            AccessScope.Organization => organizationCases,
            AccessScope.Department => organizationCases.Where(item => context.DepartmentId != null && item.DepartmentId == context.DepartmentId),
            AccessScope.Team => organizationCases.Where(item => context.TeamId != null && item.TeamId == context.TeamId),
            AccessScope.AssignedObjects => organizationCases.Where(item => db.WorkAssignments.Any(assignment => assignment.Id == item.AssignmentId
                && assignment.EmployeeId == context.EmployeeId)),
            _ => organizationCases.Where(item => item.ManagerEmployeeId == context.EmployeeId)
        };
    }
}
