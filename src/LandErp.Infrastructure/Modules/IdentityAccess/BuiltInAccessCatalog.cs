using LandErp.Application.Foundation;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.IdentityAccess;

/// <summary>Canonical built-in role grants. Synchronization is explicit setup work, never application startup work.</summary>
public static class BuiltInAccessCatalog
{
    private static readonly string[] AdministratorPermissions =
    [
        Permissions.UsersRead, Permissions.UsersManage, Permissions.OrganizationManage,
        Permissions.RolesManage, Permissions.AuditRead, Permissions.AgentsManage,
        Permissions.CollectionRead, Permissions.CollectionManage, Permissions.QueueRead
    ];

    public static IReadOnlyDictionary<string, string[]> Roles { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Owner"] = [.. AdministratorPermissions, Permissions.ManagerDecide, Permissions.HeadDecide,
                Permissions.PurchaseConfirm, Permissions.InspectionRead, Permissions.InspectionRequest, Permissions.InspectionPerform],
            ["Administrator"] = AdministratorPermissions,
            ["ProcurementManager"] = [Permissions.UsersRead, Permissions.QueueRead, Permissions.ManagerDecide, Permissions.InspectionRequest],
            ["ProcurementHead"] = [Permissions.UsersRead, Permissions.QueueRead, Permissions.HeadDecide, Permissions.PurchaseConfirm,
                Permissions.CollectionRead, Permissions.CollectionManage, Permissions.AgentsManage, Permissions.InspectionRequest],
            ["Inspector"] = [Permissions.InspectionRead, Permissions.InspectionPerform],
            ["Viewer"] = [Permissions.UsersRead, Permissions.QueueRead]
        };

    public static string[] KnownPermissions { get; } =
        Roles.Values.SelectMany(value => value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static async Task SynchronizeAsync(LandErpDbContext db, RoleManager<IdentityRole<Guid>> roleManager,
        CancellationToken cancellationToken = default)
    {
        foreach (string permission in KnownPermissions)
        {
            if (!await db.Permissions.AnyAsync(item => item.Id == permission, cancellationToken))
                db.Permissions.Add(new PermissionDefinition
                {
                    Id = permission,
                    Description = "Серверное разрешение Stage 1: " + permission
                });
        }
        await db.SaveChangesAsync(cancellationToken);

        foreach ((string name, string[] desiredPermissions) in Roles)
        {
            IdentityRole<Guid>? role = await roleManager.FindByNameAsync(name);
            if (role == null)
            {
                role = new IdentityRole<Guid>(name) { Id = DataConventions.NewId() };
                OrganizationWorkspace.EnsureIdentity(await roleManager.CreateAsync(role));
            }

            HashSet<string> desired = desiredPermissions.ToHashSet(StringComparer.Ordinal);
            RolePermissionGrant[] currentKnown = await db.RolePermissions
                .Where(item => item.RoleId == role.Id && KnownPermissions.Contains(item.PermissionId))
                .ToArrayAsync(cancellationToken);

            RolePermissionGrant[] stale = currentKnown.Where(item => !desired.Contains(item.PermissionId)).ToArray();
            if (stale.Length > 0) db.RolePermissions.RemoveRange(stale);

            HashSet<string> existing = currentKnown.Where(item => desired.Contains(item.PermissionId))
                .Select(item => item.PermissionId).ToHashSet(StringComparer.Ordinal);
            foreach (string permission in desired.Where(item => !existing.Contains(item)))
                db.RolePermissions.Add(new RolePermissionGrant { RoleId = role.Id, PermissionId = permission });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
