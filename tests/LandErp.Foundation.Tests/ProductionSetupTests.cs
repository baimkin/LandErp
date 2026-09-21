using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ProductionSetupTests
{
    [TestMethod]
    public async Task DatabaseAndFirstOwnerSetupAreRestrictedAndIdempotent()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateProductionTargetAsync();
        await ProductionDatabaseInitializer.InitializeAsync(
            sandbox.AdminConnection, sandbox.MigratorConnection, sandbox.RuntimeConnection);

        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        RoleManager<IdentityRole<Guid>> roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        await BuiltInAccessCatalog.SynchronizeAsync(db, roles);
        Assert.IsNotNull(await roles.FindByNameAsync("Inspector"));
        IdentityRole<Guid> administrator = (await roles.FindByNameAsync("Administrator"))!;
        IdentityRole<Guid> head = (await roles.FindByNameAsync("ProcurementHead"))!;
        Assert.IsTrue(await db.RolePermissions.AnyAsync(item => item.RoleId == administrator.Id && item.PermissionId == Permissions.QueueRead));
        Assert.IsFalse(await db.RolePermissions.AnyAsync(item => item.RoleId == administrator.Id && item.PermissionId == Permissions.ManagerDecide));
        Assert.IsTrue(await db.RolePermissions.AnyAsync(item => item.RoleId == head.Id && item.PermissionId == Permissions.CollectionManage));
        Assert.IsTrue(await db.RolePermissions.AnyAsync(item => item.RoleId == head.Id && item.PermissionId == Permissions.AgentsManage));

        FirstOwnerBootstrapResult created = await OwnerBootstrap.CreateFirstOwnerAsync(db, users, roles,
            "production-owner@test.invalid", "Synthetic1!ProductionOwnerPassword", "Production test");
        FirstOwnerBootstrapResult repeated = await OwnerBootstrap.CreateFirstOwnerAsync(db, users, roles,
            "production-owner@test.invalid", "Synthetic1!ProductionOwnerPassword", "Production test");
        Assert.AreEqual(FirstOwnerBootstrapResult.Created, created);
        Assert.AreEqual(FirstOwnerBootstrapResult.AlreadyExists, repeated);
        Assert.IsTrue((await users.FindByNameAsync("production-owner@test.invalid"))!.MustChangePassword);

        RolePermissionGrant? adminQueue = await db.RolePermissions.SingleOrDefaultAsync(
            item => item.RoleId == administrator.Id && item.PermissionId == Permissions.QueueRead);
        Assert.IsNotNull(adminQueue);
        db.RolePermissions.Remove(adminQueue);
        db.RolePermissions.Add(new() { RoleId = administrator.Id, PermissionId = Permissions.ManagerDecide });
        RolePermissionGrant headCollection = await db.RolePermissions.SingleAsync(
            item => item.RoleId == head.Id && item.PermissionId == Permissions.CollectionManage);
        db.RolePermissions.Remove(headCollection);
        await db.SaveChangesAsync();

        await BuiltInAccessCatalog.SynchronizeAsync(db, roles);
        await BuiltInAccessCatalog.SynchronizeAsync(db, roles);
        Assert.IsTrue(await db.RolePermissions.AnyAsync(item => item.RoleId == administrator.Id && item.PermissionId == Permissions.QueueRead));
        Assert.IsFalse(await db.RolePermissions.AnyAsync(item => item.RoleId == administrator.Id && item.PermissionId == Permissions.ManagerDecide));
        Assert.IsTrue(await db.RolePermissions.AnyAsync(item => item.RoleId == head.Id && item.PermissionId == Permissions.CollectionManage));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => OwnerBootstrap.CreateFirstOwnerAsync(
            db, users, roles, "another-owner@test.invalid", "Synthetic1!ProductionOwnerPassword", "Production test"));

        await using NpgsqlConnection runtime = new(sandbox.RuntimeConnection);
        await runtime.OpenAsync();
        await using NpgsqlCommand read = new("SELECT count(*) FROM identity.users", runtime);
        Assert.AreEqual(1L, await read.ExecuteScalarAsync());
        await using NpgsqlCommand forbidden = new("DELETE FROM foundation.audit_events", runtime);
        PostgresException denied = await Assert.ThrowsExactlyAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", denied.SqlState);
    }
}
