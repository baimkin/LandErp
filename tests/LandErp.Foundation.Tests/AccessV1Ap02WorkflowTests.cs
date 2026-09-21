using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class AccessV1Ap02WorkflowTests
{
    [TestMethod]
    public async Task InspectorOnlySeesAndPerformsOnlyAssignedInspection()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        (Subject inspector, Guid inspectorEmployeeId) = await InviteExplicitAsync(fixture,
            "ap02.inspector@test.invalid", "Inspector",
            Access(IncomingAccessLevel.None, ProcurementAccessLevel.None, CollectionAccessLevel.None,
                canPerform: true));

        Guid listingId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(
            fixture.Manager, new(listingId), "ap02-inspector-take", CancellationToken.None);
        await fixture.Workspace.AssignInspectionAsync(fixture.Manager,
            new(taken.CaseId, inspectorEmployeeId, DateTimeOffset.UtcNow.AddDays(1), "AP-02 inspector only", null),
            "ap02-inspector-assign", CancellationToken.None);

        InspectionTaskPage queue = await fixture.Workspace.ReadMyInspectionsAsync(inspector, CancellationToken.None);
        InspectionTaskItem task = queue.Items.Single();
        Assert.AreEqual(taken.CaseId, task.CaseId);

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.ReadQueueAsync(inspector, new(), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.ReadIncomingAsync(inspector, new(), CancellationToken.None));

        CollectionAdministration collection = new(fixture.Factory, TimeProvider.System);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            collection.ReadAsync(inspector, CancellationToken.None));

        InspectionWorkspaceView view = await fixture.Workspace.ReadInspectionAsync(
            inspector, taken.CaseId, CancellationToken.None);
        Assert.IsTrue(view.CanPerform);
        Assert.IsFalse(view.ReturnToProcurement);
        await fixture.Workspace.SaveInspectionAsync(inspector,
            new(taken.CaseId, view.Inspection!.Id, view.Inspection.Version, "", "", [], false),
            "ap02-inspector-start", CancellationToken.None);
        Assert.IsNotNull((await fixture.Workspace.ReadInspectionAsync(
            inspector, taken.CaseId, CancellationToken.None)).Inspection!.StartedAt);
    }

    [TestMethod]
    public async Task ReadLevelsAllowReadingButRejectMutations()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        (Subject reader, _) = await InviteExplicitAsync(fixture,
            "ap02.reader@test.invalid", "ProcurementManager",
            Access(IncomingAccessLevel.Read, ProcurementAccessLevel.Read, CollectionAccessLevel.Read,
                readScope: AccessScope.Department, workScope: AccessScope.Own));

        Guid listingId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(
            fixture.Manager, new(listingId), "ap02-reader-take", CancellationToken.None);

        IncomingCatalogPage incoming = await fixture.Workspace.ReadIncomingAsync(
            reader, new(), CancellationToken.None);
        Assert.IsTrue(incoming.Items.Any());
        ProcurementQueuePage queue = await fixture.Workspace.ReadQueueAsync(reader, new(), CancellationToken.None);
        Assert.IsTrue(queue.Items.Any(item => item.CaseId == taken.CaseId));

        CollectionAdministration collection = new(fixture.Factory, TimeProvider.System);
        _ = await collection.ReadAsync(reader, CancellationToken.None);

        CatalogItemView listing = (await fixture.Workspace.ReadItemAsync(
            reader, listingId, CancellationToken.None)).Item;
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.RegisterViewAsync(reader, listingId, "ap02-read-view", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.SetDispositionAsync(reader,
                new(listingId, listing.Version, CatalogDisposition.Dismissed, "read-only"),
                "ap02-read-incoming-deny", CancellationToken.None));

        CaseCard card = await fixture.Workspace.ReadCardAsync(reader, taken.CaseId, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.AddNoteAsync(reader,
                new(taken.CaseId, card.Item.CaseVersion, "read-only", false, "", null),
                "ap02-read-procurement-deny", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            collection.CreateGroupAsync(reader, "read-only", 10, "ap02-read-collection-deny", CancellationToken.None));

    }

    [TestMethod]
    public async Task IncomingOnlyTransfersToEffectiveProcurementRecipientWithoutGainingProcurement()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        (Subject incoming, Guid incomingEmployeeId) = await InviteExplicitAsync(fixture,
            "ap02.incoming@test.invalid", "ProcurementManager",
            Access(IncomingAccessLevel.Process, ProcurementAccessLevel.None, CollectionAccessLevel.None));
        (Subject recipient, Guid recipientEmployeeId) = await InviteExplicitAsync(fixture,
            "ap02.recipient@test.invalid", "Inspector",
            Access(IncomingAccessLevel.None, ProcurementAccessLevel.Manager, CollectionAccessLevel.None));

        IEmployeeAccessService resolver = fixture.Scope.ServiceProvider.GetRequiredService<IEmployeeAccessService>();
        Assert.AreEqual(EmployeeAccessSource.Configured,
            (await resolver.ResolveAsync(fixture.Manager, CancellationToken.None)).Source,
            "AP-04 fixtures must resolve from explicit EmployeeAccessSettings.");

        Guid listingId = await fixture.Workspace.CreateManualAsync(incoming,
            new(CatalogSource.Other, "Incoming-only AP-02", "Химки", 2_500_000m, 1200m,
                null, null, null, "Передача в закупку", "AP-02"),
            "ap02-incoming-create", CancellationToken.None);

        IReadOnlyList<IncomingProcurementTarget> targets =
            await fixture.Workspace.ReadProcurementTargetsAsync(incoming, CancellationToken.None);
        Assert.IsTrue(targets.Any(item => item.EmployeeId == recipientEmployeeId),
            "Explicit ProcurementAccess must make a recipient eligible regardless of legacy role.");
        Assert.IsFalse(targets.Any(item => item.EmployeeId == incomingEmployeeId),
            "Explicit ProcurementAccess=None must override the legacy ProcurementManager role.");

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.TakeToWorkAsync(incoming, new(listingId), "ap02-no-self-take", CancellationToken.None));

        TakeToWorkResult transferred = await fixture.Workspace.TransferToProcurementAsync(incoming,
            new(listingId, recipientEmployeeId), "ap02-transfer", CancellationToken.None);

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.ReadQueueAsync(incoming, new(), CancellationToken.None));

        ProcurementQueuePage recipientQueue = await fixture.Workspace.ReadQueueAsync(
            recipient, new(), CancellationToken.None);
        Assert.IsTrue(recipientQueue.Items.Any(item => item.CaseId == transferred.CaseId));
        CaseCard card = await fixture.Workspace.ReadCardAsync(recipient, transferred.CaseId, CancellationToken.None);
        Assert.AreEqual(recipientEmployeeId, card.ManagerEmployeeId);
    }

    [TestMethod]
    public async Task ReadScopeCanBeWiderThanWorkScopeAndCollectionDoesNotGrantProcurement()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        await fixture.SetExplicitAccessAsync(fixture.ManagerEmployeeId,
            Access(IncomingAccessLevel.Process, ProcurementAccessLevel.Manager, CollectionAccessLevel.Manage,
                readScope: AccessScope.Department, workScope: AccessScope.AssignedObjects,
                canAssign: true));

        Guid otherListing = await fixture.Workspace.CreateManualAsync(fixture.SecondManager,
            new(CatalogSource.Other, "Чужой объект AP-02", "Лобня", 3_000_000m, 1000m,
                null, null, null, null, "AP-02"), "ap02-other-create", CancellationToken.None);
        TakeToWorkResult otherCase = await fixture.Workspace.TakeToWorkAsync(
            fixture.SecondManager, new(otherListing), "ap02-other-take", CancellationToken.None);

        ProcurementQueuePage readable = await fixture.Workspace.ReadQueueAsync(
            fixture.Manager, new(), CancellationToken.None);
        Assert.IsTrue(readable.Items.Any(item => item.CaseId == otherCase.CaseId),
            "Department ReadScope must include another employee's case in the same department.");

        CaseCard otherCard = await fixture.Workspace.ReadCardAsync(
            fixture.Manager, otherCase.CaseId, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.AddNoteAsync(fixture.Manager,
                new(otherCase.CaseId, otherCard.Item.CaseVersion, "Недопустимая правка", false, "", null),
                "ap02-work-scope-deny", CancellationToken.None));

        Guid ownListing = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Other, "Свой объект AP-02", "Химки", 3_200_000m, 1100m,
                null, null, null, null, "AP-02"), "ap02-own-create", CancellationToken.None);
        TakeToWorkResult ownCase = await fixture.Workspace.TakeToWorkAsync(
            fixture.Manager, new(ownListing), "ap02-own-take", CancellationToken.None);

        CollectionAdministration collection = new(fixture.Factory, TimeProvider.System);
        _ = await collection.ReadAsync(fixture.Manager, CancellationToken.None);
        _ = await collection.CreateGroupAsync(
            fixture.Manager, "AP-02 группа", 10, "ap02-group", CancellationToken.None);

        CaseCard ownCard = await fixture.Workspace.ReadCardAsync(
            fixture.Manager, ownCase.CaseId, CancellationToken.None);
        Assert.IsFalse(ownCard.CanConfirmPurchase);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.MarkAcquiredAsync(fixture.Manager,
                new(ownCase.CaseId, ownCard.Item.CaseVersion, 3_100_000m,
                    DateOnly.FromDateTime(DateTime.UtcNow), ""),
                "ap02-purchase-deny", CancellationToken.None));

        (Subject inspector, Guid inspectorEmployeeId) = await InviteExplicitAsync(fixture,
            "ap02.scope.inspector@test.invalid", "Inspector",
            Access(IncomingAccessLevel.None, ProcurementAccessLevel.None, CollectionAccessLevel.None,
                canPerform: true));
        await fixture.Workspace.AssignInspectionAsync(fixture.Manager,
            new(ownCase.CaseId, inspectorEmployeeId, DateTimeOffset.UtcNow.AddDays(1), "", null),
            "ap02-scope-inspection", CancellationToken.None);
        InspectionWorkspaceView managerInspection = await fixture.Workspace.ReadInspectionAsync(
            fixture.Manager, ownCase.CaseId, CancellationToken.None);
        Assert.IsFalse(managerInspection.CanPerform,
            "Procurement Manager must not inherit inspection execution when CanPerformInspections=false.");
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.SaveInspectionAsync(fixture.Manager,
                new(ownCase.CaseId, managerInspection.Inspection!.Id,
                    managerInspection.Inspection.Version, "", "", [], false),
                "ap02-manager-inspection-deny", CancellationToken.None));

        (Subject collectionOnly, _) = await InviteExplicitAsync(fixture,
            "ap02.collection.only@test.invalid", "Inspector",
            Access(IncomingAccessLevel.None, ProcurementAccessLevel.None, CollectionAccessLevel.Manage));
        _ = await collection.ReadAsync(collectionOnly, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.ReadQueueAsync(collectionOnly, new(), CancellationToken.None));

        _ = inspector; // The assigned inspector is intentionally independent of Procurement access.
    }

    [TestMethod]
    public async Task PurchaseCapabilityAddsOnlyPurchaseForAccessibleCase()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await fixture.SetExplicitAccessAsync(fixture.ManagerEmployeeId,
            Access(IncomingAccessLevel.Process, ProcurementAccessLevel.Manager, CollectionAccessLevel.Manage,
                readScope: AccessScope.Department, workScope: AccessScope.Department,
                canConfirm: true));

        Guid listingId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Other, "Покупка AP-02", "Химки", 4_000_000m, 1400m,
                null, null, null, null, "AP-02"), "ap02-buy-create", CancellationToken.None);
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(
            fixture.Manager, new(listingId), "ap02-buy-take", CancellationToken.None);

        CaseCard before = await fixture.Workspace.ReadCardAsync(
            fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.IsTrue(before.CanConfirmPurchase);
        Assert.IsFalse(before.CanHeadDecide);
        Assert.IsFalse(before.CanManageTemplates);

        await fixture.Workspace.MarkAcquiredAsync(fixture.Manager,
            new(taken.CaseId, before.Item.CaseVersion, 3_900_000m,
                DateOnly.FromDateTime(DateTime.UtcNow), "AP-02 purchase capability"),
            "ap02-buy", CancellationToken.None);

        CaseCard after = await fixture.Workspace.ReadCardAsync(
            fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual("acquired", after.Item.Stage);
        Assert.AreEqual(3_900_000m, after.AcquisitionPrice);
    }

    [TestMethod]
    public async Task HeadCanDoManagerWorkButCannotApproveOwnRequest()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid headEmployeeId = fixture.EmployeeId("head-phase1@test.invalid");
        await fixture.SetExplicitAccessAsync(headEmployeeId,
            Access(IncomingAccessLevel.None, ProcurementAccessLevel.Head, CollectionAccessLevel.None,
                readScope: AccessScope.Organization, workScope: AccessScope.Organization));

        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Head,
            new("Head manager action", "Химки", null, 5_000_000m, 1500m, "AP-02 head"),
            "ap02-head-create", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Head, created.CaseId, CancellationToken.None);
        Assert.IsTrue(card.CanManagerDecide, "Head must include ordinary Manager work.");

        await fixture.Workspace.AddNoteAsync(fixture.Head,
            new(created.CaseId, card.Item.CaseVersion, "Head выполняет обычное действие менеджера", false, "", null),
            "ap02-head-note", CancellationToken.None);

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            PropertyCase propertyCase = await db.PropertyCases.SingleAsync(item => item.Id == created.CaseId);
            propertyCase.StageId = "pending_head";
            propertyCase.PendingApprovalId = Guid.CreateVersion7();
            db.Entry(propertyCase).Property(item => item.Version).IsModified = true;
            await db.SaveChangesAsync();
        }

        card = await fixture.Workspace.ReadCardAsync(fixture.Head, created.CaseId, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.DecideAsync(fixture.Head,
                new(created.CaseId, card.Item.CaseVersion, 0, ProcurementAction.Approve,
                    "Самосогласование запрещено", "", null, null),
                "ap02-head-self-approve", CancellationToken.None));
    }

    [TestMethod]
    public async Task HandoverIncludesIncompleteInspectionAndUsesEffectivePerformerAccess()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        (Subject firstInspector, Guid firstInspectorId) = await InviteExplicitAsync(fixture,
            "ap02.handover.one@test.invalid", "Inspector",
            Access(IncomingAccessLevel.None, ProcurementAccessLevel.None, CollectionAccessLevel.None,
                canPerform: true));
        (Subject secondInspector, Guid secondInspectorId) = await InviteExplicitAsync(fixture,
            "ap02.handover.two@test.invalid", "ProcurementManager",
            Access(IncomingAccessLevel.None, ProcurementAccessLevel.None, CollectionAccessLevel.None,
                canPerform: true));

        Guid listingId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(
            fixture.Manager, new(listingId), "ap02-handover-take", CancellationToken.None);
        await fixture.Workspace.AssignInspectionAsync(fixture.Manager,
            new(taken.CaseId, firstInspectorId, DateTimeOffset.UtcNow.AddDays(1), "handover", null),
            "ap02-handover-assign", CancellationToken.None);

        EmployeeWorkImpact impact = await fixture.Organization.ReadEmployeeWorkImpactAsync(
            fixture.Owner, firstInspectorId, CancellationToken.None);
        Assert.AreEqual(1, impact.OpenInspections);
        Assert.IsTrue(impact.Candidates.Any(item => item.EmployeeId == secondInspectorId),
            "Candidate selection must use effective CanPerformInspections, not the legacy role.");

        await fixture.Organization.TransferEmployeeWorkAsync(fixture.Owner,
            new(firstInspectorId, secondInspectorId), "ap02-handover", CancellationToken.None);

        Assert.AreEqual(0, (await fixture.Workspace.ReadMyInspectionsAsync(
            firstInspector, CancellationToken.None)).Items.Count);
        Assert.AreEqual(1, (await fixture.Workspace.ReadMyInspectionsAsync(
            secondInspector, CancellationToken.None)).Items.Count);
    }

    private static EmployeeAccessConfiguration Access(
        IncomingAccessLevel incoming,
        ProcurementAccessLevel procurement,
        CollectionAccessLevel collection,
        AccessScope readScope = AccessScope.Organization,
        AccessScope workScope = AccessScope.Organization,
        bool canAssign = false,
        bool canPerform = false,
        bool canConfirm = false,
        bool canManageTemplates = false) =>
        new(incoming, procurement, readScope, workScope, collection,
            canAssign, canPerform, canConfirm, canManageTemplates, CanReadAudit: false);

    private static async Task<(Subject Subject, Guid EmployeeId)> InviteExplicitAsync(
        ProcurementTests.Phase1Fixture fixture,
        string login,
        string role,
        EmployeeAccessConfiguration settings)
    {
        OrganizationView structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        Guid userId = await ProcurementTestsHelper.InviteAsync(
            fixture.Services, fixture.Organization, fixture.Owner, structure,
            login, login, role, fixture.DepartmentA, AccessScope.Own, settings);
        structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        Guid employeeId = structure.Employees.Single(item => item.Login == login).Id;
        return (new Subject(userId, false), employeeId);
    }
}
