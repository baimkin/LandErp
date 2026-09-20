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
        => await OwnerBootstrap.CreateOwnerAsync(db, users, roles, login, password, organizationName,
            mustChangePassword: false, correlationId: "local-bootstrap");
}

public enum FirstOwnerBootstrapResult { Created, AlreadyExists }

/// <summary>Explicit setup-only bootstrap shared by Local and the production operator tool.</summary>
public static class OwnerBootstrap
{
    public static async Task<FirstOwnerBootstrapResult> CreateFirstOwnerAsync(LandErpDbContext db,
        UserManager<LandErpUser> users, RoleManager<IdentityRole<Guid>> roles, string login, string password,
        string organizationName)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(4815162342)");
        int usersCount = await db.Users.CountAsync();
        int organizationsCount = await db.Organizations.CountAsync();
        if (usersCount != 0 || organizationsCount != 0)
        {
            LandErpUser? existing = await users.FindByNameAsync(login);
            Guid? employeeId = existing == null ? null : await db.Employees
                .Where(item => item.UserId == existing.Id && item.Active)
                .Select(item => (Guid?)item.Id).SingleOrDefaultAsync();
            bool complete = usersCount == 1 && organizationsCount == 1 && existing != null
                && await db.Organizations.AnyAsync(item => item.Name == organizationName)
                && await users.IsInRoleAsync(existing, "Owner")
                && employeeId != null
                && await db.EmployeeAssignments.AnyAsync(item => item.EmployeeId == employeeId
                    && item.Scope == AccessScope.Organization);
            if (!complete)
                throw new InvalidOperationException("First Owner can only be initialized in an empty database; existing identity data requires manual review.");
            await transaction.CommitAsync();
            return FirstOwnerBootstrapResult.AlreadyExists;
        }

        await CreateOwnerCoreAsync(db, users, roles, login, password, organizationName,
            mustChangePassword: true, correlationId: "production-bootstrap");
        await transaction.CommitAsync();
        return FirstOwnerBootstrapResult.Created;
    }

    public static async Task<Guid> CreateOwnerAsync(LandErpDbContext db, UserManager<LandErpUser> users,
        RoleManager<IdentityRole<Guid>> roles, string login, string password, string organizationName,
        bool mustChangePassword, string correlationId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        Guid result = await CreateOwnerCoreAsync(db, users, roles, login, password, organizationName,
            mustChangePassword, correlationId);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task<Guid> CreateOwnerCoreAsync(LandErpDbContext db, UserManager<LandErpUser> users,
        RoleManager<IdentityRole<Guid>> roles, string login, string password, string organizationName,
        bool mustChangePassword, string correlationId)
    {
        string[] admin = [Permissions.UsersRead, Permissions.UsersManage, Permissions.OrganizationManage,
            Permissions.RolesManage, Permissions.AuditRead, Permissions.AgentsManage,
            Permissions.CollectionRead, Permissions.CollectionManage];
        Dictionary<string, string[]> catalog = new(StringComparer.Ordinal)
        {
            ["Owner"] = [.. admin, Permissions.QueueRead, Permissions.ManagerDecide, Permissions.HeadDecide, Permissions.PurchaseConfirm],
            ["Administrator"] = admin,
            ["ProcurementManager"] = [Permissions.UsersRead, Permissions.QueueRead, Permissions.ManagerDecide],
            ["ProcurementHead"] = [Permissions.UsersRead, Permissions.QueueRead, Permissions.HeadDecide, Permissions.PurchaseConfirm],
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

        LandErpUser user = new() { Id = DataConventions.NewId(), UserName = login, Email = login,
            EmailConfirmed = true, MustChangePassword = mustChangePassword, LockoutEnabled = mustChangePassword };
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
            new(user.Id, false), "OwnerBootstrapped", "Employee", employee.Id, new { Role = "Owner" }, correlationId);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
