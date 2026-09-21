using System.Text.Json;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;
using LandErp.Server.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class AccessV1Ap03UiTests
{
    [TestMethod]
    public async Task OrganizationReadsAndSavesExplicitAccessWithAuditAndConcurrency()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid employeeId = fixture.ManagerEmployeeId;

        OrganizationView before = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        EmployeeView beforeEmployee = before.Employees.Single(item => item.Id == employeeId);
        Assert.AreEqual(EmployeeAccessSource.LegacyPermissions, beforeEmployee.Access.Source);
        Assert.IsNull(beforeEmployee.Access.Version);

        EmployeeAccessConfiguration configured = Access(
            IncomingAccessLevel.Process, ProcurementAccessLevel.Manager, CollectionAccessLevel.Manage,
            AccessScope.Department, AccessScope.AssignedObjects,
            canAssign: true, canPerform: false, canConfirm: true, canTemplates: true, canAudit: true);

        EmployeeAccessView saved = await fixture.Organization.SaveEmployeeAccessAsync(
            fixture.Owner, new(employeeId, null, configured), "ap03-access-save", CancellationToken.None);

        Assert.AreEqual(EmployeeAccessSource.Configured, saved.Source);
        Assert.IsNotNull(saved.Version);
        Assert.AreEqual(configured, saved.Settings);

        OrganizationView after = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        EmployeeAccessView fromOrganizationUi = after.Employees.Single(item => item.Id == employeeId).Access;
        Assert.AreEqual(EmployeeAccessSource.Configured, fromOrganizationUi.Source);
        Assert.AreEqual(saved.Version, fromOrganizationUi.Version);
        Assert.AreEqual(configured, fromOrganizationUi.Settings);

        IAuditReadService auditReader = fixture.Scope.ServiceProvider.GetRequiredService<IAuditReadService>();
        AuditPage visibleAudit = await auditReader.ReadAsync(fixture.Manager, new(PageSize: 100), CancellationToken.None);
        AuditEventView accessAudit = visibleAudit.Items.Single(item => item.Title == "Изменены настройки Access V1 сотрудника");
        Assert.IsTrue(accessAudit.Changes.Any(item => item.Field == "Читать полный аудит"
            && item.Before == "Нет" && item.After == "Да"));

        await using LandErpDbContext db = fixture.Sandbox.Context();
        var audit = await db.AuditEvents.AsNoTracking()
            .SingleAsync(item => item.ObjectId == employeeId && item.Action == "EmployeeAccessChanged");
        using JsonDocument payload = JsonDocument.Parse(audit.Changes);
        Assert.IsTrue(payload.RootElement.TryGetProperty("Before", out JsonElement previous));
        Assert.IsTrue(payload.RootElement.TryGetProperty("After", out JsonElement current));
        Assert.AreEqual(nameof(EmployeeAccessSource.LegacyPermissions),
            payload.RootElement.GetProperty("BeforeSource").GetString());
        Assert.AreEqual((int)ProcurementAccessLevel.Manager,
            previous.GetProperty("ProcurementAccess").GetInt32());
        Assert.AreEqual((int)CollectionAccessLevel.Manage,
            current.GetProperty("CollectionAccess").GetInt32());

        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() =>
            fixture.Organization.SaveEmployeeAccessAsync(
                fixture.Owner, new(employeeId, null, configured with { CanConfirmPurchase = false }),
                "ap03-stale", CancellationToken.None));
    }

    [TestMethod]
    public async Task ServerRejectsInvalidScopeAndProtectsOwner()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);

        EmployeeAccessConfiguration invalid = Access(
            IncomingAccessLevel.Read, ProcurementAccessLevel.Manager, CollectionAccessLevel.None,
            AccessScope.Team, AccessScope.Organization);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            fixture.Organization.SaveEmployeeAccessAsync(
                fixture.Owner, new(fixture.ManagerEmployeeId, null, invalid),
                "ap03-invalid-scope", CancellationToken.None));

        Guid ownerEmployeeId = (await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None))
            .Employees.Single(item => item.Role == "Owner").Id;
        EmployeeAccessView owner = await fixture.Organization.ReadEmployeeAccessAsync(
            fixture.Owner, ownerEmployeeId, CancellationToken.None);
        Assert.IsTrue(owner.SystemProtected);
        Assert.AreEqual(EmployeeAccessSource.SystemOwner, owner.Source);

        EmployeeAccessConfiguration restricted = Access(
            IncomingAccessLevel.None, ProcurementAccessLevel.None, CollectionAccessLevel.None,
            AccessScope.Own, AccessScope.Own);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            fixture.Organization.SaveEmployeeAccessAsync(
                fixture.Owner, new(ownerEmployeeId, owner.Version, restricted),
                "ap03-owner-protection", CancellationToken.None));
    }

    [TestMethod]
    public async Task AccessReductionCannotOrphanActiveInspectionOrProcurementWork()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        OrganizationView structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        const string inspectorLogin = "ap03.inspector@test.invalid";
        Guid inspectorUserId = await ProcurementTestsHelper.InviteAsync(
            fixture.Services, fixture.Organization, fixture.Owner, structure,
            "AP03 Inspector", inspectorLogin, "Inspector", fixture.DepartmentA, AccessScope.Own);
        _ = inspectorUserId;
        structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        Guid inspectorEmployeeId = structure.Employees.Single(item => item.Login == inspectorLogin).Id;

        EmployeeAccessConfiguration inspectorAccess = Access(
            IncomingAccessLevel.None, ProcurementAccessLevel.None, CollectionAccessLevel.None,
            AccessScope.Own, AccessScope.Own, canPerform: true);
        EmployeeAccessView inspectorSaved = await fixture.Organization.SaveEmployeeAccessAsync(
            fixture.Owner, new(inspectorEmployeeId, null, inspectorAccess),
            "ap03-inspector-access", CancellationToken.None);

        Guid listingId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(
            fixture.Manager, new(listingId), "ap03-take", CancellationToken.None);
        await fixture.Workspace.AssignInspectionAsync(
            fixture.Manager,
            new(taken.CaseId, inspectorEmployeeId, DateTimeOffset.UtcNow.AddDays(1), "AP03 active inspection", null),
            "ap03-assign-inspection", CancellationToken.None);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            fixture.Organization.SaveEmployeeAccessAsync(
                fixture.Owner,
                new(inspectorEmployeeId, inspectorSaved.Version,
                    inspectorAccess with { CanPerformInspections = false }),
                "ap03-disable-inspection", CancellationToken.None));

        await using (LandErpDbContext db = fixture.Sandbox.Context())
        {
            SiteInspection inspection = await db.SiteInspections.AsNoTracking()
                .SingleAsync(item => item.PropertyCaseId == taken.CaseId);
            Assert.AreEqual(inspectorEmployeeId, inspection.InspectorEmployeeId,
                "Rejected access reduction must leave the real assignment intact for explicit handover.");
        }

        EmployeeAccessView managerAccess = await fixture.Organization.ReadEmployeeAccessAsync(
            fixture.Owner, fixture.ManagerEmployeeId, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            fixture.Organization.SaveEmployeeAccessAsync(
                fixture.Owner,
                new(fixture.ManagerEmployeeId, managerAccess.Version,
                    managerAccess.Settings with { ProcurementAccess = ProcurementAccessLevel.None }),
                "ap03-drop-procurement", CancellationToken.None));
    }

    [TestMethod]
    public void UiPresetsAreFormValuesAndCanBeCustomized()
    {
        AccessV1Preset manager = AccessV1Ui.Presets.Single(item => item.Id == "procurement-manager");
        EmployeeAccessConfiguration customized = manager.Settings with
        {
            IncomingAccess = IncomingAccessLevel.Process,
            CollectionAccess = CollectionAccessLevel.Manage,
            CanConfirmPurchase = true
        };

        Assert.AreEqual(ProcurementAccessLevel.Manager, manager.Settings.ProcurementAccess);
        Assert.AreEqual(IncomingAccessLevel.None, manager.Settings.IncomingAccess,
            "Changing the form copy must not mutate the preset definition.");
        Assert.AreEqual(IncomingAccessLevel.Process, customized.IncomingAccess);
        Assert.IsTrue(customized.CanConfirmPurchase);
        Assert.AreEqual(6, AccessV1Ui.Presets.Count);
        Assert.AreEqual(ProcurementAccessLevel.Head,
            AccessV1Ui.Presets.Single(item => item.Id == "procurement-head").Settings.ProcurementAccess);
    }

    [TestMethod]
    public void NavigationMatchesAgreedWorkerCombinations()
    {
        AccessV1NavigationState inspector = AccessV1Ui.Navigation(Effective(
            Access(IncomingAccessLevel.None, ProcurementAccessLevel.None, CollectionAccessLevel.None,
                AccessScope.Own, AccessScope.Own, canPerform: true)));
        Assert.IsFalse(inspector.Incoming);
        Assert.IsFalse(inspector.Procurement);
        Assert.IsTrue(inspector.Inspections);
        Assert.IsFalse(inspector.Collection);

        AccessV1NavigationState incoming = AccessV1Ui.Navigation(Effective(
            Access(IncomingAccessLevel.Process, ProcurementAccessLevel.None, CollectionAccessLevel.None)));
        Assert.IsTrue(incoming.Incoming);
        Assert.IsFalse(incoming.Procurement);
        Assert.IsFalse(incoming.Inspections);

        AccessV1NavigationState combined = AccessV1Ui.Navigation(Effective(
            Access(IncomingAccessLevel.Process, ProcurementAccessLevel.Manager, CollectionAccessLevel.Manage,
                AccessScope.Department, AccessScope.AssignedObjects)));
        Assert.IsTrue(combined.Incoming);
        Assert.IsTrue(combined.Procurement);
        Assert.IsTrue(combined.Collection);
        Assert.IsFalse(combined.Inspections);

        AccessV1NavigationState purchase = AccessV1Ui.Navigation(Effective(
            Access(IncomingAccessLevel.Process, ProcurementAccessLevel.Manager, CollectionAccessLevel.Manage,
                AccessScope.Department, AccessScope.AssignedObjects, canConfirm: true)));
        Assert.AreEqual(combined, purchase,
            "Purchase capability must not implicitly add another navigation section.");
    }

    [TestMethod]
    public void ReadOnlyEffectiveAccessKeepsSectionsVisibleWithoutManageCapabilities()
    {
        EffectiveEmployeeAccess readOnly = Effective(Access(
            IncomingAccessLevel.Read, ProcurementAccessLevel.Read, CollectionAccessLevel.Read,
            AccessScope.Department, AccessScope.Own));

        AccessV1NavigationState navigation = AccessV1Ui.Navigation(readOnly);
        Assert.IsTrue(navigation.Incoming);
        Assert.IsTrue(navigation.Procurement);
        Assert.IsTrue(navigation.Collection);
        Assert.IsFalse(readOnly.CanProcessIncoming);
        Assert.IsFalse(readOnly.CanManageProcurement);
        Assert.IsFalse(readOnly.CanManageCollection);

        EmployeeAccessConfiguration legacyAuditSettings = readOnly.Settings with
        {
            ProcurementReadScope = AccessScope.Department,
            ProcurementWorkScope = AccessScope.Own,
            CanReadAudit = true
        };
        EffectiveEmployeeAccess legacyDepartment = readOnly with
        {
            Settings = legacyAuditSettings,
            Source = EmployeeAccessSource.LegacyPermissions
        };
        Assert.IsFalse(legacyDepartment.CanReadAudit,
            "Legacy audit fallback must preserve the historical organization-scope requirement until AP-04.");
        Assert.IsTrue((legacyDepartment with
        {
            Settings = legacyAuditSettings with { ProcurementReadScope = AccessScope.Organization }
        }).CanReadAudit);
    }

    [TestMethod]
    public void UiSourceUsesEffectiveCapabilitiesInsteadOfLegacyWorkflowRolePolicies()
    {
        string root = FoundationTests.RepositoryRoot();
        string incoming = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components", "Pages", "IncomingCatalogV2.razor"));
        string collectors = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components", "Pages", "Collectors.razor"));
        string inspections = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components", "Pages", "SiteInspectionPage.razor"));
        string procurementQueue = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components", "Pages", "ProcurementQueueV2.razor"));
        string shell = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components", "Layout", "AppShell.razor"));
        string caseWorkspace = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components", "Procurement", "CaseWorkspace.razor"));

        StringAssert.Contains(incoming, "CanProcessIncoming");
        StringAssert.Contains(incoming, "Передать в закупку");
        StringAssert.Contains(incoming, "manualOpen && CanProcessIncoming");
        StringAssert.Contains(collectors, "CanManageCollection");
        Assert.IsFalse(collectors.Contains("Policy=\"searches.manage\"", StringComparison.Ordinal));
        Assert.IsFalse(collectors.Contains("Policy=\"agents.manage\"", StringComparison.Ordinal));

        StringAssert.Contains(inspections, "правом выполнять осмотры");
        Assert.IsFalse(inspections.Contains("ролью Inspector", StringComparison.Ordinal));

        Assert.IsFalse(shell.Contains("Policy=\"manager_queue.read\"", StringComparison.Ordinal));
        Assert.IsFalse(shell.Contains("Policy=\"inspections.read\"", StringComparison.Ordinal));
        Assert.IsFalse(shell.Contains("Policy=\"collection.read\"", StringComparison.Ordinal));
        Assert.IsFalse(shell.Contains("Policy=\"audit.read\"", StringComparison.Ordinal));

        StringAssert.Contains(caseWorkspace, "card.CanManagerDecide||card.CanHeadDecide");
        StringAssert.Contains(caseWorkspace, "card.CanAssignInspections||card.CanPerformInspections");
        StringAssert.Contains(caseWorkspace, "card.CanPerformInspections&&");
        StringAssert.Contains(procurementQueue, "detail.CanAssignInspections");
        StringAssert.Contains(procurementQueue, "detail.CanPerformInspections");
    }

    private static EffectiveEmployeeAccess Effective(EmployeeAccessConfiguration settings) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            settings, EmployeeAccessSource.Configured);

    private static EmployeeAccessConfiguration Access(
        IncomingAccessLevel incoming,
        ProcurementAccessLevel procurement,
        CollectionAccessLevel collection,
        AccessScope readScope = AccessScope.Organization,
        AccessScope workScope = AccessScope.Organization,
        bool canAssign = false,
        bool canPerform = false,
        bool canConfirm = false,
        bool canTemplates = false,
        bool canAudit = false) =>
        new(incoming, procurement, readScope, workScope, collection,
            canAssign, canPerform, canConfirm, canTemplates, canAudit);
}
