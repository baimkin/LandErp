using LandErp.Application.Foundation;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.IdentityAccess;

/// <summary>Explicit migrator-only command, never called by application startup.</summary>
public static class LocalBootstrap
{
    public static async Task<Guid> CreateOwnerAsync(LandErpDbContext db, UserManager<LandErpUser> users,
        RoleManager<IdentityRole<Guid>> roles, string login, string password, string organizationName)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        string[] admin = [Permissions.UsersRead, Permissions.UsersManage, Permissions.OrganizationManage,
            Permissions.RolesManage, Permissions.AuditRead, Permissions.AgentsManage,
            Permissions.CollectionRead, Permissions.CollectionManage];
        Dictionary<string, string[]> catalog = new(StringComparer.Ordinal)
        {
            ["Owner"] = [.. admin, Permissions.QueueRead, Permissions.ManagerDecide, Permissions.HeadDecide],
            ["Administrator"] = admin,
            ["ProcurementManager"] = [Permissions.UsersRead, Permissions.QueueRead, Permissions.ManagerDecide],
            ["ProcurementHead"] = [Permissions.UsersRead, Permissions.QueueRead, Permissions.HeadDecide],
            ["Viewer"] = [Permissions.UsersRead, Permissions.QueueRead]
        };
        foreach (string permission in catalog.Values.SelectMany(item => item).Distinct(StringComparer.Ordinal))
        {
            if (!await db.Permissions.AnyAsync(item => item.Id == permission))
            {
                db.Permissions.Add(new() { Id = permission, Description = "Серверное разрешение Stage 1: " + permission });
            }
        }

        await db.SaveChangesAsync();
        foreach ((string name, string[] permissions) in catalog)
        {
            IdentityRole<Guid>? role = await roles.FindByNameAsync(name);
            if (role == null)
            {
                role = new(name) { Id = DataConventions.NewId() };
                Organization.OrganizationWorkspace.EnsureIdentity(await roles.CreateAsync(role));
            }

            foreach (string permission in permissions)
            {
                if (!await db.RolePermissions.AnyAsync(item => item.RoleId == role.Id && item.PermissionId == permission))
                {
                    db.RolePermissions.Add(new() { RoleId = role.Id, PermissionId = permission });
                }
            }
        }

        LandErpUser user = new() { Id = DataConventions.NewId(), UserName = login, Email = login, EmailConfirmed = true };
        Organization.OrganizationWorkspace.EnsureIdentity(await users.CreateAsync(user, password));
        Organization.OrganizationWorkspace.EnsureIdentity(await users.AddToRoleAsync(user, "Owner"));
        var organization = new LandErp.Application.Modules.Organization.Domain.Organization
        {
            Id = DataConventions.NewId(), Name = organizationName
        };
        Employee employee = new() { Id = DataConventions.NewId(), OrganizationId = organization.Id,
            UserId = user.Id, DisplayName = "Владелец", Active = true };
        db.Organizations.Add(organization);
        db.Employees.Add(employee);
        db.EmployeeAssignments.Add(new() { Id = DataConventions.NewId(), EmployeeId = employee.Id,
            RoleId = (await roles.FindByNameAsync("Owner"))!.Id, Scope = AccessScope.Organization });
        Organization.OrganizationWorkspace.AddAudit(db, new(employee.Id, organization.Id, null, null, AccessScope.Organization),
            new(user.Id, false), "OwnerBootstrapped", "Employee", employee.Id, new { Role = "Owner" }, "local-bootstrap");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return user.Id;
    }
}
