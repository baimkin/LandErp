using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
    public async Task LegacyFallbackExplicitSettingsPersistenceInvariantAndOwnerProtection()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext db = sandbox.Context()) await db.Database.MigrateAsync();

        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerUserId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "access-v1-owner", "Access V1 organization");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerUserId);

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
                    managerRoleId, AccessScope.Department),
                "access-v1", CancellationToken.None);

            await using LandErpDbContext db = sandbox.Context();
            Guid managerUserId = await db.Employees.Where(item => item.Id == manager.EmployeeId)
                .Select(item => item.UserId).SingleAsync();

            await sandbox.GrantRuntimeAsync();
            await using ServiceProvider runtime = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
            await using AsyncServiceScope runtimeScope = runtime.CreateAsyncScope();
            IEmployeeAccessService access = runtimeScope.ServiceProvider.GetRequiredService<IEmployeeAccessService>();

            EffectiveEmployeeAccess legacy = await access.ResolveAsync(new Subject(managerUserId, false), CancellationToken.None);
            Assert.AreEqual(EmployeeAccessSource.LegacyPermissions, legacy.Source);
            Assert.AreEqual(IncomingAccessLevel.Process, legacy.Settings.IncomingAccess);
            Assert.AreEqual(ProcurementAccessLevel.Manager, legacy.Settings.ProcurementAccess);
            Assert.AreEqual(AccessScope.Department, legacy.Settings.ProcurementReadScope);
            Assert.AreEqual(AccessScope.Department, legacy.Settings.ProcurementWorkScope);
            Assert.AreEqual(CollectionAccessLevel.None, legacy.Settings.CollectionAccess);
            Assert.IsTrue(legacy.Settings.CanAssignInspections);
            Assert.IsFalse(legacy.Settings.CanPerformInspections);
            Assert.IsFalse(legacy.Settings.CanConfirmPurchase);
            Assert.IsTrue(legacy.Settings.CanManageTemplates);
            Assert.IsFalse(legacy.Settings.CanReadAudit);

            db.EmployeeAccessSettings.Add(new EmployeeAccessSettings
            {
                EmployeeId = manager.EmployeeId,
                IncomingAccess = IncomingAccessLevel.Read,
                ProcurementAccess = ProcurementAccessLevel.Read,
                ProcurementReadScope = AccessScope.Department,
                ProcurementWorkScope = AccessScope.AssignedObjects,
                CollectionAccess = CollectionAccessLevel.Read,
                CanPerformInspections = true,
                Version = 1
            });
            await db.SaveChangesAsync();

            EffectiveEmployeeAccess configured = await access.ResolveAsync(new Subject(managerUserId, false), CancellationToken.None);
            Assert.AreEqual(EmployeeAccessSource.Configured, configured.Source);
            Assert.AreEqual(IncomingAccessLevel.Read, configured.Settings.IncomingAccess);
            Assert.AreEqual(ProcurementAccessLevel.Read, configured.Settings.ProcurementAccess);
            Assert.AreEqual(AccessScope.Department, configured.ProcurementReadContext.Scope);
            Assert.AreEqual(AccessScope.AssignedObjects, configured.ProcurementWorkContext.Scope);
            Assert.AreEqual(CollectionAccessLevel.Read, configured.Settings.CollectionAccess);
            Assert.IsTrue(configured.Settings.CanPerformInspections);
            Assert.IsFalse(configured.Settings.CanAssignInspections);

            EmployeeAccessSettings stored = await db.EmployeeAccessSettings.SingleAsync(item => item.EmployeeId == manager.EmployeeId);
            stored.ProcurementReadScope = AccessScope.Team;
            stored.ProcurementWorkScope = AccessScope.Organization;
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => db.SaveChangesAsync());

            Guid ownerEmployeeId = await db.Employees.Where(item => item.UserId == ownerUserId)
                .Select(item => item.Id).SingleAsync();
            db.Entry(stored).State = EntityState.Unchanged;
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
    }
}
