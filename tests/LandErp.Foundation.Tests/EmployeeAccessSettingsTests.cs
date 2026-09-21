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
        await using (LandErpDbContext migratorDb = sandbox.Context()) await migratorDb.Database.MigrateAsync();

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
    public async Task CutoverMigrationBackfillsRoleMatrixPreservesExplicitSettingsAndExcludesOwner()
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

        Guid managerEmployeeId;
        Guid headEmployeeId;
        Guid inspectorEmployeeId;
        Guid administratorEmployeeId;
        Guid secondOwnerEmployeeId;
        Guid explicitEmployeeId;
        EmployeeAccessConfiguration preserved = new(
            IncomingAccessLevel.Read, ProcurementAccessLevel.Read,
            AccessScope.AssignedObjects, AccessScope.Own, CollectionAccessLevel.Read,
            false, true, false, true, true);

        await using (AsyncServiceScope setupScope = bootstrap.CreateAsyncScope())
        {
            IOrganizationWorkspace workspace = setupScope.ServiceProvider.GetRequiredService<IOrganizationWorkspace>();
            Subject owner = new(ownerUserId, true);
            await workspace.CreateDepartmentAsync(owner, "Закупка", "cutover", CancellationToken.None);
            OrganizationView organization = await workspace.ReadAsync(owner, CancellationToken.None);
            Guid departmentId = organization.Departments.Single().Id;
            Guid managerRoleId = organization.Roles.Single(item => item.Name == "ProcurementManager").Id;
            Guid headRoleId = organization.Roles.Single(item => item.Name == "ProcurementHead").Id;
            Guid inspectorRoleId = organization.Roles.Single(item => item.Name == "Inspector").Id;
            Guid administratorRoleId = organization.Roles.Single(item => item.Name == "Administrator").Id;
            Guid ownerRoleId = organization.Roles.Single(item => item.Name == "Owner").Id;

            managerEmployeeId = (await workspace.CreateEmployeeAsync(owner,
                new("Backfill manager", "cutover.manager", departmentId, null, null, null,
                    managerRoleId, AccessScope.Department),
                "cutover-manager", CancellationToken.None)).EmployeeId;
            headEmployeeId = (await workspace.CreateEmployeeAsync(owner,
                new("Backfill head", "cutover.head", departmentId, null, null, null,
                    headRoleId, AccessScope.Organization),
                "cutover-head", CancellationToken.None)).EmployeeId;
            inspectorEmployeeId = (await workspace.CreateEmployeeAsync(owner,
                new("Backfill inspector", "cutover.inspector", departmentId, null, null, null,
                    inspectorRoleId, AccessScope.Own),
                "cutover-inspector", CancellationToken.None)).EmployeeId;
            administratorEmployeeId = (await workspace.CreateEmployeeAsync(owner,
                new("Backfill administrator", "cutover.admin", null, null, null, null,
                    administratorRoleId, AccessScope.Organization),
                "cutover-admin", CancellationToken.None)).EmployeeId;
            secondOwnerEmployeeId = (await workspace.CreateEmployeeAsync(owner,
                new("Second owner", "cutover.owner2", null, null, null, null,
                    ownerRoleId, AccessScope.Organization),
                "cutover-owner", CancellationToken.None)).EmployeeId;
            explicitEmployeeId = (await workspace.CreateEmployeeAsync(owner,
                new("Explicit manager", "cutover.explicit", departmentId, null, null, null,
                    managerRoleId, AccessScope.Department, true, preserved),
                "cutover-explicit", CancellationToken.None)).EmployeeId;
        }

        await using (LandErpDbContext db = sandbox.Context())
        {
            Guid[] backfillIds =
            [
                managerEmployeeId, headEmployeeId, inspectorEmployeeId, administratorEmployeeId
            ];
            EmployeeAccessSettings[] generated = await db.EmployeeAccessSettings
                .Where(item => backfillIds.Contains(item.EmployeeId)).ToArrayAsync();
            Assert.AreEqual(backfillIds.Length, generated.Length);
            db.EmployeeAccessSettings.RemoveRange(generated);
            Assert.IsFalse(await db.EmployeeAccessSettings.AnyAsync(item => item.EmployeeId == secondOwnerEmployeeId));
            await db.SaveChangesAsync();

            IMigrator migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync();
        }

        await using (LandErpDbContext verified = sandbox.Context())
        {
            EmployeeAccessSettings manager = await verified.EmployeeAccessSettings.AsNoTracking()
                .SingleAsync(item => item.EmployeeId == managerEmployeeId);
            Assert.AreEqual(IncomingAccessLevel.Process, manager.IncomingAccess);
            Assert.AreEqual(ProcurementAccessLevel.Manager, manager.ProcurementAccess);
            Assert.AreEqual(AccessScope.Department, manager.ProcurementReadScope);
            Assert.AreEqual(AccessScope.Department, manager.ProcurementWorkScope);
            Assert.AreEqual(CollectionAccessLevel.None, manager.CollectionAccess);
            Assert.IsTrue(manager.CanAssignInspections);
            Assert.IsFalse(manager.CanPerformInspections);
            Assert.IsFalse(manager.CanConfirmPurchase);
            Assert.IsTrue(manager.CanManageTemplates);
            Assert.IsFalse(manager.CanReadAudit);

            EmployeeAccessSettings head = await verified.EmployeeAccessSettings.AsNoTracking()
                .SingleAsync(item => item.EmployeeId == headEmployeeId);
            Assert.AreEqual(IncomingAccessLevel.Read, head.IncomingAccess);
            Assert.AreEqual(ProcurementAccessLevel.Head, head.ProcurementAccess);
            Assert.AreEqual(AccessScope.Organization, head.ProcurementReadScope);
            Assert.AreEqual(AccessScope.Organization, head.ProcurementWorkScope);
            Assert.AreEqual(CollectionAccessLevel.Manage, head.CollectionAccess);
            Assert.IsTrue(head.CanAssignInspections);
            Assert.IsFalse(head.CanPerformInspections);
            Assert.IsTrue(head.CanConfirmPurchase);
            Assert.IsTrue(head.CanManageTemplates);
            Assert.IsFalse(head.CanReadAudit);

            EmployeeAccessSettings inspector = await verified.EmployeeAccessSettings.AsNoTracking()
                .SingleAsync(item => item.EmployeeId == inspectorEmployeeId);
            Assert.AreEqual(IncomingAccessLevel.None, inspector.IncomingAccess);
            Assert.AreEqual(ProcurementAccessLevel.None, inspector.ProcurementAccess);
            Assert.AreEqual(AccessScope.Own, inspector.ProcurementReadScope);
            Assert.AreEqual(AccessScope.Own, inspector.ProcurementWorkScope);
            Assert.AreEqual(CollectionAccessLevel.None, inspector.CollectionAccess);
            Assert.IsFalse(inspector.CanAssignInspections);
            Assert.IsTrue(inspector.CanPerformInspections);
            Assert.IsFalse(inspector.CanConfirmPurchase);
            Assert.IsFalse(inspector.CanManageTemplates);
            Assert.IsFalse(inspector.CanReadAudit);

            EmployeeAccessSettings administrator = await verified.EmployeeAccessSettings.AsNoTracking()
                .SingleAsync(item => item.EmployeeId == administratorEmployeeId);
            Assert.AreEqual(IncomingAccessLevel.Read, administrator.IncomingAccess);
            Assert.AreEqual(ProcurementAccessLevel.Read, administrator.ProcurementAccess);
            Assert.AreEqual(AccessScope.Organization, administrator.ProcurementReadScope);
            Assert.AreEqual(AccessScope.Organization, administrator.ProcurementWorkScope);
            Assert.AreEqual(CollectionAccessLevel.Manage, administrator.CollectionAccess);
            Assert.IsFalse(administrator.CanAssignInspections);
            Assert.IsFalse(administrator.CanPerformInspections);
            Assert.IsFalse(administrator.CanConfirmPurchase);
            Assert.IsFalse(administrator.CanManageTemplates);
            Assert.IsTrue(administrator.CanReadAudit);

            Assert.IsFalse(await verified.EmployeeAccessSettings.AsNoTracking()
                .AnyAsync(item => item.EmployeeId == secondOwnerEmployeeId));

            EmployeeAccessSettings explicitStored = await verified.EmployeeAccessSettings.AsNoTracking()
                .SingleAsync(item => item.EmployeeId == explicitEmployeeId);
            Assert.AreEqual(preserved.IncomingAccess, explicitStored.IncomingAccess);
            Assert.AreEqual(preserved.ProcurementAccess, explicitStored.ProcurementAccess);
            Assert.AreEqual(preserved.ProcurementReadScope, explicitStored.ProcurementReadScope);
            Assert.AreEqual(preserved.ProcurementWorkScope, explicitStored.ProcurementWorkScope);
            Assert.AreEqual(preserved.CollectionAccess, explicitStored.CollectionAccess);
            Assert.AreEqual(preserved.CanAssignInspections, explicitStored.CanAssignInspections);
            Assert.AreEqual(preserved.CanPerformInspections, explicitStored.CanPerformInspections);
            Assert.AreEqual(preserved.CanConfirmPurchase, explicitStored.CanConfirmPurchase);
            Assert.AreEqual(preserved.CanManageTemplates, explicitStored.CanManageTemplates);
            Assert.AreEqual(preserved.CanReadAudit, explicitStored.CanReadAudit);

            Assert.IsFalse(verified.Database.HasPendingModelChanges());
        }
    }

}
