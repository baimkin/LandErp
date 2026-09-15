using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Collector.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class IncomingMonitoringTests
{
    [TestMethod]
    public async Task IncomingBrowserSupportsManualDetailMonitoringClassificationAndResponsiveLayout()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await ProcurementUiScenario.RunPhase3Async(fixture.Sandbox, "manager-phase1@test.invalid");
    }

    [TestMethod]
    public async Task TotalPriceReactivatesAttentionAndResumesTheSameRejectedCase()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var ingested = await fixture.IngestMarketplacePairAsync();
        Guid avitoId = ingested.AvitoId;
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(avitoId), "phase3-take", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        await fixture.Workspace.DecideAsync(fixture.Manager, new(taken.CaseId, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Reject, "Пока слишком дорого", "", null, null), "phase3-reject", CancellationToken.None);

        CatalogItemDetail before = await fixture.Workspace.ReadItemAsync(fixture.Manager, avitoId, CancellationToken.None);
        await fixture.Workspace.SetMonitoringAsync(fixture.Manager,
            new(avitoId, before.Item.Version, 1_900_000m, null, "Вернуть при снижении общей цены"),
            "phase3-monitor-total", CancellationToken.None);
        await fixture.IngestChangedAsync(ListingSource.Avito, ingested.Agent, ingested.Administration, 1_800_000m, "phase3-total-trigger");

        CatalogItemDetail triggered = await fixture.Workspace.ReadItemAsync(fixture.Manager, avitoId, CancellationToken.None);
        Assert.AreEqual(CatalogDisposition.Incoming, triggered.Item.Disposition);
        Assert.IsTrue(triggered.Item.AttentionRequired);
        Assert.IsTrue(triggered.Events.Any(item => item.Kind == CatalogEventKind.MonitoringTriggered));
        Assert.AreEqual(2_000_000m, (await fixture.ReadCaseAsync(taken.CaseId)).WorkingPrice,
            "Source price changes must not overwrite verified case-owned facts.");
        await Assert.ThrowsExactlyAsync<LandErp.Application.Modules.IdentityAccess.Contracts.AccessDeniedException>(
            () => fixture.Workspace.ReadItemAsync(fixture.ForeignOwner, avitoId, CancellationToken.None));

        TakeToWorkResult resumed = await fixture.Workspace.ResumeCaseAsync(fixture.Manager,
            new(avitoId, triggered.Item.Version), "phase3-resume", CancellationToken.None);
        Assert.AreEqual(taken.CaseId, resumed.CaseId);
        Assert.IsFalse(resumed.Created);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.PropertyCases.CountAsync()));
        Assert.AreEqual("analysis", (await fixture.ReadCaseAsync(taken.CaseId)).StageId);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.WorkflowTransitions.CountAsync(item => item.Action == "Resume")));
    }

    [TestMethod]
    public async Task PricePerSotkaThresholdUsesCurrentPriceAndArea()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var ingested = await fixture.IngestMarketplacePairAsync();
        Guid cianId = ingested.CianId;
        CatalogItemDetail before = await fixture.Workspace.ReadItemAsync(fixture.Manager, cianId, CancellationToken.None);
        Assert.AreEqual(decimal.Round(2_000_000m * 100m / 1_500m, 4), before.Item.PricePerSotka);
        await fixture.Workspace.SetMonitoringAsync(fixture.Manager,
            new(cianId, before.Item.Version, null, 120_000m, "Вернуть по цене за сотку"),
            "phase3-monitor-sotka", CancellationToken.None);

        await fixture.IngestChangedAsync(ListingSource.Cian, ingested.Agent, ingested.Administration, 1_700_000m, "phase3-sotka-trigger");
        CatalogItemDetail triggered = await fixture.Workspace.ReadItemAsync(fixture.Manager, cianId, CancellationToken.None);
        Assert.AreEqual(CatalogDisposition.Incoming, triggered.Item.Disposition);
        Assert.IsTrue(triggered.Item.AttentionRequired);
        Assert.IsTrue(triggered.Monitoring.LastEvaluatedPricePerSotka <= 120_000m);
        StringAssert.Contains(triggered.Item.QueueReason, "сотку");
    }

    [TestMethod]
    public async Task ClassificationsRemainDistinctAndIncomingFiltersArePagedAndTenantSafe()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        CatalogDisposition[] classifications = [CatalogDisposition.Duplicate, CatalogDisposition.Fake,
            CatalogDisposition.RemovedAtSource, CatalogDisposition.Sold];
        foreach (CatalogDisposition classification in classifications)
        {
            Guid id = await fixture.Workspace.CreateManualAsync(fixture.Manager,
                new(CatalogSource.Telegram, classification + " предложение", "Химки", 2_000_000m, 1_000m,
                    null, null, null, null, "Проверка классификации"), "phase3-manual", CancellationToken.None);
            CatalogItemDetail item = await fixture.Workspace.ReadItemAsync(fixture.Manager, id, CancellationToken.None);
            Assert.IsNull(item.Item.Url);
            Assert.IsNull(item.Item.ExternalId);
            await fixture.Workspace.SetDispositionAsync(fixture.Manager,
                new(id, item.Item.Version, classification, "Подтверждено в тесте"), "phase3-classify", CancellationToken.None);
        }

        IncomingCatalogPage all = await fixture.Workspace.ReadIncomingAsync(fixture.Manager,
            new(Source: CatalogSource.Telegram, Disposition: null, MinPrice: 1_900_000m, MaxPrice: 2_100_000m,
                MinAreaSquareMeters: 900m, MaxAreaSquareMeters: 1_100m, Offset: 1, Size: 2), CancellationToken.None);
        Assert.AreEqual(4, all.Total);
        Assert.AreEqual(2, all.Items.Count);
        CollectionAssert.AreEquivalent(classifications,
            (await fixture.Workspace.ReadIncomingAsync(fixture.Manager, new(Disposition: null), CancellationToken.None))
                .Items.Select(item => item.Disposition).Distinct().ToArray());
        Assert.AreEqual(0, (await fixture.Workspace.ReadIncomingAsync(fixture.ForeignOwner,
            new(Disposition: null), CancellationToken.None)).Total);
        await Assert.ThrowsExactlyAsync<LandErp.Application.Modules.IdentityAccess.Contracts.AccessDeniedException>(() =>
            fixture.Workspace.SetDispositionAsync(fixture.ForeignOwner,
                new(all.Items[0].Id, all.Items[0].Version, CatalogDisposition.Dismissed, "Чужая организация"),
                "phase3-foreign", CancellationToken.None));
    }
}
