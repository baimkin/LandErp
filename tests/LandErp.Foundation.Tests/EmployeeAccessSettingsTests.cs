using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class EmployeeAccessRulesTests
{
    [TestMethod]
    public void ProcurementWorkScopeCannotExceedReadScope()
    {
        EmployeeAccessConfiguration valid = new(
            IncomingAccessLevel.Read,
            ProcurementAccessLevel.Manager,
            AccessScope.Department,
            AccessScope.Team,
            CollectionAccessLevel.Read,
            CanAssignInspections: false,
            CanPerformInspections: true,
            CanConfirmPurchase: false,
            CanManageTemplates: true,
            CanReadAudit: false);

        EmployeeAccessRules.Validate(valid);
        Assert.IsTrue(EmployeeAccessRules.ContainsScope(AccessScope.Department, AccessScope.Team));
        Assert.IsFalse(EmployeeAccessRules.ContainsScope(AccessScope.Team, AccessScope.Department));

        EmployeeAccessConfiguration invalid = valid with
        {
            ProcurementReadScope = AccessScope.Team,
            ProcurementWorkScope = AccessScope.Organization
        };
        Assert.ThrowsExactly<ArgumentException>(() => EmployeeAccessRules.Validate(invalid));
    }
}

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class EmployeeAccessSettingsTests
{
    [TestMethod]
    public async Task ExplicitSettingsAreRequiredPersistedAndOwnerProtected()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext db = sandbox.Context()) await db.Database.MigrateAsync();

        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerUserId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "access-v1-owner", "Access V1 organization");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerUserId);

        EmployeeAccessConfiguration managerAccess = new(
            IncomingAccessLevel.Process,
            ProcurementAccessLevel.Manager,
            AccessScope.Department,
            AccessScope.Department,
            CollectionAccessLevel.None,
            CanAssignInspections: true,
            CanPerformInspections: false,
            CanConfirmPurchase: false,
            CanManageTemplates: true,
            CanReadAudit: false);

        Guid managerEmployeeId;
        Guid managerUserId;
        await using (AsyncServiceScope setupScope = bootstrap.CreateAsyncScope())
        {
            IOrganizationWorkspace workspace = setupScope.ServiceProvider.GetRequiredService<IOrganizationWorkspace>();
            Subject owner = new(ownerUserId, true);
            await workspace.CreateDepartmentAsync(owner, "Закупка", "access-v1", CancellationToken.None);
            OrganizationView organization = await workspace.ReadAsync(owner, CancellationToken.None);
            Guid departmentId = organization.Departments.Single().Id;
            Guid managerRoleId = organization.Roles.Single(item => item.Name == "ProcurementManager").Id;
            TemporaryCredential manager = await workspace.CreateEmployeeAsync(owner,
                new("Менеджер Access V1", "access.v1.manager", departmentId, null, null, null,
                    managerRoleId, AccessScope.Department, true, managerAccess),
                "access-v1", CancellationToken.None);
            managerEmployeeId = manager.EmployeeId;

            await using LandErpDbContext setupDb = sandbox.Context();
            managerUserId = await setupDb.Employees.Where(item => item.Id == managerEmployeeId)
                .Select(item => item.UserId).SingleAsync();
        }

        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider runtime = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        await using AsyncServiceScope runtimeScope = runtime.CreateAsyncScope();
        IEmployeeAccessService access = runtimeScope.ServiceProvider.GetRequiredService<IEmployeeAccessService>();

        EffectiveEmployeeAccess configured = await access.ResolveAsync(new Subject(managerUserId, false), CancellationToken.None);
        Assert.AreEqual(EmployeeAccessSource.Configured, configured.Source);
        Assert.AreEqual(managerAccess, configured.Settings);

        await using LandErpDbContext db = sandbox.Context();
        EmployeeAccessSettings stored = await db.EmployeeAccessSettings.SingleAsync(item => item.EmployeeId == managerEmployeeId);
        stored.IncomingAccess = IncomingAccessLevel.Read;
        stored.ProcurementAccess = ProcurementAccessLevel.Read;
        stored.ProcurementWorkScope = AccessScope.AssignedObjects;
        stored.CollectionAccess = CollectionAccessLevel.Read;
        stored.CanAssignInspections = false;
        stored.CanPerformInspections = true;
        stored.CanManageTemplates = false;
        await db.SaveChangesAsync();

        configured = await access.ResolveAsync(new Subject(managerUserId, false), CancellationToken.None);
        Assert.AreEqual(IncomingAccessLevel.Read, configured.Settings.IncomingAccess);
        Assert.AreEqual(ProcurementAccessLevel.Read, configured.Settings.ProcurementAccess);
        Assert.AreEqual(AccessScope.Department, configured.ProcurementReadContext.Scope);
        Assert.AreEqual(AccessScope.AssignedObjects, configured.ProcurementWorkContext.Scope);
        Assert.AreEqual(CollectionAccessLevel.Read, configured.Settings.CollectionAccess);
        Assert.IsTrue(configured.Settings.CanPerformInspections);
        Assert.IsFalse(configured.Settings.CanAssignInspections);

        stored.ProcurementReadScope = AccessScope.Team;
        stored.ProcurementWorkScope = AccessScope.Organization;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => db.SaveChangesAsync());
        db.Entry(stored).State = EntityState.Unchanged;

        db.EmployeeAccessSettings.Remove(stored);
        await db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            access.ResolveAsync(new Subject(managerUserId, false), CancellationToken.None));

        Guid ownerEmployeeId = await db.Employees.Where(item => item.UserId == ownerUserId)
            .Select(item => item.Id).SingleAsync();
        db.EmployeeAccessSettings.Add(new EmployeeAccessSettings
        {
            EmployeeId = ownerEmployeeId,
            IncomingAccess = IncomingAccessLevel.None,
            ProcurementAccess = ProcurementAccessLevel.None,
            ProcurementReadScope = AccessScope.Own,
            ProcurementWorkScope = AccessScope.Own,
            CollectionAccess = CollectionAccessLevel.None,
            Version = 1
        });
        await db.SaveChangesAsync();

        EffectiveEmployeeAccess ownerAccess = await access.ResolveAsync(new Subject(ownerUserId, true), CancellationToken.None);
        Assert.AreEqual(EmployeeAccessSource.SystemOwner, ownerAccess.Source);
        Assert.IsTrue(ownerAccess.IsSystemOwner);
        Assert.AreEqual(IncomingAccessLevel.Process, ownerAccess.Settings.IncomingAccess);
        Assert.AreEqual(ProcurementAccessLevel.Head, ownerAccess.Settings.ProcurementAccess);
        Assert.AreEqual(AccessScope.Organization, ownerAccess.Settings.ProcurementReadScope);
        Assert.AreEqual(AccessScope.Organization, ownerAccess.Settings.ProcurementWorkScope);
        Assert.AreEqual(CollectionAccessLevel.Manage, ownerAccess.Settings.CollectionAccess);
        Assert.IsTrue(ownerAccess.Settings.CanAssignInspections);
        Assert.IsTrue(ownerAccess.Settings.CanPerformInspections);
        Assert.IsTrue(ownerAccess.Settings.CanConfirmPurchase);
        Assert.IsTrue(ownerAccess.Settings.CanManageTemplates);
        Assert.IsTrue(ownerAccess.Settings.CanReadAudit);
    }

    [TestMethod]
    public async Task CutoverMigrationBackfillsMissingRowsAndPreservesExplicitSettings()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext preCutover = sandbox.Context())
        {
            IMigrator migrator = preCutover.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260921183000_EmployeeAccessSettings");
        }

        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerUserId = await IdentityOrganizationTests.BootstrapAsync(
            bootstrap, "access-v1-cutover-owner", "Access V1 cutover organization");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerUserId);

        Guid missingEmployeeId;
        Guid explicitEmployeeId;
        EmployeeAccessConfiguration preserved = new(
            IncomingAccessLevel.Read, ProcurementAccessLevel.Read,
            AccessScope.AssignedObjects, AccessScope.Own, CollectionAccessLevel.Read,
            false, true, false, false, false);

        await using (AsyncServiceScope setupScope = bootstrap.CreateAsyncScope())
        {
            IOrganizationWorkspace workspace = setupScope.ServiceProvider.GetRequiredService<IOrganizationWorkspace>();
            Subject owner = new(ownerUserId, true);
            await workspace.CreateDepartmentAsync(owner, "Закупка", "cutover", CancellationToken.None);
            OrganizationView organization = await workspace.ReadAsync(owner, CancellationToken.None);
            Guid departmentId = organization.Departments.Single().Id;
            Guid managerRoleId = organization.Roles.Single(item => item.Name == "ProcurementManager").Id;

            TemporaryCredential missing = await workspace.CreateEmployeeAsync(owner,
                new("Backfill manager", "cutover.backfill", departmentId, null, null, null,
                    managerRoleId, AccessScope.Department),
                "cutover-missing", CancellationToken.None);
            missingEmployeeId = missing.EmployeeId;

            TemporaryCredential explicitEmployee = await workspace.CreateEmployeeAsync(owner,
                new("Explicit manager", "cutover.explicit", departmentId, null, null, null,
                    managerRoleId, AccessScope.Department, true, preserved),
                "cutover-explicit", CancellationToken.None);
            explicitEmployeeId = explicitEmployee.EmployeeId;
        }

        await using (LandErpDbContext db = sandbox.Context())
        {
            EmployeeAccessSettings missing = await db.EmployeeAccessSettings.SingleAsync(
                item => item.EmployeeId == missingEmployeeId);
            db.EmployeeAccessSettings.Remove(missing);
            await db.SaveChangesAsync();

            IMigrator migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync();
        }

        await using (LandErpDbContext verified = sandbox.Context())
        {
            EmployeeAccessSettings backfilled = await verified.EmployeeAccessSettings.AsNoTracking()
                .SingleAsync(item => item.EmployeeId == missingEmployeeId);
            Assert.AreEqual(IncomingAccessLevel.Process, backfilled.IncomingAccess);
            Assert.AreEqual(ProcurementAccessLevel.Manager, backfilled.ProcurementAccess);
            Assert.AreEqual(AccessScope.Department, backfilled.ProcurementReadScope);
            Assert.AreEqual(AccessScope.Department, backfilled.ProcurementWorkScope);
            Assert.IsTrue(backfilled.CanAssignInspections);
            Assert.IsTrue(backfilled.CanManageTemplates);

            EmployeeAccessSettings explicitStored = await verified.EmployeeAccessSettings.AsNoTracking()
                .SingleAsync(item => item.EmployeeId == explicitEmployeeId);
            Assert.AreEqual(preserved.IncomingAccess, explicitStored.IncomingAccess);
            Assert.AreEqual(preserved.ProcurementAccess, explicitStored.ProcurementAccess);
            Assert.AreEqual(preserved.ProcurementReadScope, explicitStored.ProcurementReadScope);
            Assert.AreEqual(preserved.ProcurementWorkScope, explicitStored.ProcurementWorkScope);
            Assert.AreEqual(preserved.CollectionAccess, explicitStored.CollectionAccess);
            Assert.AreEqual(preserved.CanPerformInspections, explicitStored.CanPerformInspections);
        }
    }

}
