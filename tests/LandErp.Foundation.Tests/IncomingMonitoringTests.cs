using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Persistence;
using LandErp.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

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
    public async Task ResumeAvailabilityFollowsLinkedCaseLifecycleNotSourceDisposition()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var ingested = await fixture.IngestMarketplacePairAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager,
            new(ingested.AvitoId), "resume-lifecycle-take", CancellationToken.None);

        await DecideAsync(fixture, taken.CaseId, ProcurementAction.Reject, "PropertyCase отклонён");
        CatalogItemDetail rejected = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.AvitoId, CancellationToken.None);
        Assert.AreEqual("rejected", rejected.Item.LinkedCaseStage);
        Assert.IsTrue(rejected.Item.CanResumeCase, "Rejected PropertyCase must offer resume regardless of source disposition.");

        await fixture.Workspace.ResumeCaseAsync(fixture.Manager,
            new(ingested.AvitoId, rejected.Item.Version), "resume-rejected", CancellationToken.None);
        await DecideAsync(fixture, taken.CaseId, ProcurementAction.Monitor, "PropertyCase приостановлен");
        CatalogItemDetail paused = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.AvitoId, CancellationToken.None);
        Assert.AreEqual("monitor", paused.Item.LinkedCaseStage);
        Assert.IsTrue(paused.Item.CanResumeCase, "Paused PropertyCase must offer resume regardless of source disposition.");

        await fixture.Workspace.ResumeCaseAsync(fixture.Manager,
            new(ingested.AvitoId, paused.Item.Version), "resume-monitor", CancellationToken.None);
        foreach (CatalogDisposition disposition in new[]
                 {
                     CatalogDisposition.RemovedAtSource,
                     CatalogDisposition.Sold,
                     CatalogDisposition.Fake,
                     CatalogDisposition.Duplicate
                 })
        {
            CatalogItemDetail active = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.AvitoId, CancellationToken.None);
            await fixture.Workspace.SetDispositionAsync(fixture.Manager,
                new(ingested.AvitoId, active.Item.Version, disposition, $"Источник: {disposition}"),
                "classify-active-source", CancellationToken.None);
            active = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.AvitoId, CancellationToken.None);
            Assert.AreEqual("analysis", active.Item.LinkedCaseStage);
            Assert.IsFalse(active.Item.CanResumeCase,
                $"Active PropertyCase must not offer resume for source disposition {disposition}.");
        }
    }

    [TestMethod]
    public async Task MonitoringImmediatelyReactivatesWhenCurrentTotalPriceMeetsThreshold()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var ingested = await fixture.IngestMarketplacePairAsync();
        CatalogItemDetail before = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.AvitoId, CancellationToken.None);

        await fixture.Workspace.SetMonitoringAsync(fixture.Manager,
            new(ingested.AvitoId, before.Item.Version, 2_100_000m, null, "Текущая общая цена уже подходит"),
            "monitor-immediate-total", CancellationToken.None);

        CatalogItemDetail result = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.AvitoId, CancellationToken.None);
        Assert.AreEqual(CatalogDisposition.Incoming, result.Item.Disposition);
        Assert.IsTrue(result.Item.AttentionRequired);
        Assert.AreEqual(2_000_000m, result.Monitoring.LastEvaluatedPrice);
        Assert.IsTrue(result.Events.Any(item => item.Kind == CatalogEventKind.MonitoringTriggered));
    }

    [TestMethod]
    public async Task SourcePriceChangeStoresBeforeAfterAndShowsDelta()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var ingested = await fixture.IngestMarketplacePairAsync();
        IIncomingCatalogReadService reads = fixture.Scope.ServiceProvider.GetRequiredService<IIncomingCatalogReadService>();

        await fixture.IngestChangedAvitoAsync(ingested.Agent, ingested.Administration, 1_800_000m);

        CatalogItemDetail detail = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.AvitoId, CancellationToken.None);
        CatalogEventView priceEvent = detail.Events.Single(item => item.Kind == CatalogEventKind.SourceChanged);
        Assert.AreEqual(2_000_000m, priceEvent.PreviousObservedPrice);
        Assert.AreEqual(1_800_000m, priceEvent.ObservedPrice);
        Assert.AreEqual(decimal.Round(2_000_000m * 100m / 1_500m, 4), priceEvent.PreviousObservedPricePerSotka);
        Assert.AreEqual(decimal.Round(1_800_000m * 100m / 1_500m, 4), priceEvent.ObservedPricePerSotka);
        StringAssert.Contains(detail.Item.QueueReason, "2");
        StringAssert.Contains(detail.Item.QueueReason, "1");
        StringAssert.Contains(detail.Item.QueueReason, "−200");
        StringAssert.Contains(detail.Item.QueueReason, "−10%");

        IncomingCatalogReadPage changedPage = await reads.ReadAsync(fixture.Manager,
            new(new(), Preset: IncomingCatalogPreset.PriceChanged), CancellationToken.None);
        Assert.IsTrue(changedPage.Items.Any(item => item.Id == ingested.AvitoId));
    }

    [TestMethod]
    public async Task MonitoringImmediatelyReactivatesWhenCurrentPricePerSotkaMeetsThreshold()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var ingested = await fixture.IngestMarketplacePairAsync();
        CatalogItemDetail before = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.CianId, CancellationToken.None);

        await fixture.Workspace.SetMonitoringAsync(fixture.Manager,
            new(ingested.CianId, before.Item.Version, null, 140_000m, "Текущая цена за сотку уже подходит"),
            "monitor-immediate-sotka", CancellationToken.None);

        CatalogItemDetail result = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.CianId, CancellationToken.None);
        Assert.AreEqual(CatalogDisposition.Incoming, result.Item.Disposition);
        Assert.IsTrue(result.Item.AttentionRequired);
        Assert.IsTrue(result.Monitoring.LastEvaluatedPricePerSotka <= 140_000m);
        Assert.IsTrue(result.Events.Any(item => item.Kind == CatalogEventKind.MonitoringTriggered));
    }

    [TestMethod]
    public async Task MonitoringRemainsPausedWhenNoCurrentThresholdIsMet()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var ingested = await fixture.IngestMarketplacePairAsync();
        CatalogItemDetail before = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.AvitoId, CancellationToken.None);

        await fixture.Workspace.SetMonitoringAsync(fixture.Manager,
            new(ingested.AvitoId, before.Item.Version, 1_900_000m, 120_000m, "Оба порога пока не достигнуты"),
            "monitor-not-reached", CancellationToken.None);

        CatalogItemDetail result = await fixture.Workspace.ReadItemAsync(fixture.Manager, ingested.AvitoId, CancellationToken.None);
        Assert.AreEqual(CatalogDisposition.Monitoring, result.Item.Disposition);
        Assert.IsFalse(result.Item.AttentionRequired);
        Assert.AreEqual(2_000_000m, result.Monitoring.LastEvaluatedPrice);
        Assert.IsTrue(result.Monitoring.LastEvaluatedPricePerSotka > 120_000m);
        Assert.IsFalse(result.Events.Any(item => item.Kind == CatalogEventKind.MonitoringTriggered));
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

    [TestMethod]
    public async Task ReviewStateIsGlobalAndEveryDetailOpenIsAudited()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        var ingested = await fixture.IngestMarketplacePairAsync();
        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Access, fixture.Workspace, TimeProvider.System);

        IncomingCatalogReadPage before = await reads.ReadAsync(fixture.Manager, new(new()), CancellationToken.None);
        Assert.AreEqual(2, before.Summary.New);

        await fixture.Workspace.RegisterViewAsync(fixture.Manager, ingested.AvitoId, "view-manager", CancellationToken.None);
        IncomingCatalogReadPage afterFirst = await reads.ReadAsync(fixture.Manager, new(new()), CancellationToken.None);
        Assert.AreEqual(1, afterFirst.Summary.New);
        Assert.IsTrue(afterFirst.Rows[ingested.AvitoId].Reviewed);

        IncomingCatalogReadPage sharedNew = await reads.ReadAsync(fixture.SecondManager,
            new(new(), Preset: IncomingCatalogPreset.New), CancellationToken.None);
        Assert.AreEqual(1, sharedNew.Total);
        Assert.AreEqual(ingested.CianId, sharedNew.Items.Single().Id,
            "New state is shared by the organization, not per manager.");

        await fixture.Workspace.RegisterViewAsync(fixture.SecondManager, ingested.AvitoId, "view-second-manager", CancellationToken.None);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.CatalogEvents.CountAsync(item =>
            item.CatalogItemId == ingested.AvitoId && item.Kind == CatalogEventKind.ReviewStarted)));
        Assert.AreEqual(2, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item =>
            item.ObjectId == ingested.AvitoId && item.Action == "CatalogItemViewed")));

        IAuditReadService audit = fixture.Scope.ServiceProvider.GetRequiredService<IAuditReadService>();
        AuditPage auditPage = await audit.ReadAsync(fixture.Owner, new(PageSize: 100), CancellationToken.None);
        Assert.AreEqual(2, auditPage.Items.Count(item => item.Title == "Просмотрено входящее предложение"));
    }

    [TestMethod]
    public async Task ProcessedTodayCountsDecisionsButNotSimpleViews()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Access, fixture.Workspace, TimeProvider.System);

        Guid classifiedId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "Классифицировать", "Химки", 2_000_000m, 1_000m,
                null, null, null, null, "processed-today"), "processed-classified-create", CancellationToken.None);
        CatalogItemDetail classified = await fixture.Workspace.ReadItemAsync(fixture.Manager, classifiedId, CancellationToken.None);
        await fixture.Workspace.SetDispositionAsync(fixture.Manager,
            new(classifiedId, classified.Item.Version, CatalogDisposition.Fake, "Подтверждённый фейк"),
            "processed-classified", CancellationToken.None);

        Guid monitoredId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "Мониторинг", "Химки", 2_000_000m, 1_000m,
                null, null, null, null, "processed-today"), "processed-monitor-create", CancellationToken.None);
        CatalogItemDetail monitored = await fixture.Workspace.ReadItemAsync(fixture.Manager, monitoredId, CancellationToken.None);
        await fixture.Workspace.SetMonitoringAsync(fixture.Manager,
            new(monitoredId, monitored.Item.Version, 1_500_000m, null, "Ждём снижения"),
            "processed-monitor", CancellationToken.None);

        Guid takenId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "В Procurement", "Химки", 2_000_000m, 1_000m,
                null, null, null, null, "processed-today"), "processed-take-create", CancellationToken.None);
        await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(takenId), "processed-take", CancellationToken.None);

        Guid viewedOnlyId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "Только просмотр", "Химки", 2_000_000m, 1_000m,
                null, null, null, null, "processed-today"), "processed-view-create", CancellationToken.None);
        await fixture.Workspace.RegisterViewAsync(fixture.Manager, viewedOnlyId, "processed-view", CancellationToken.None);

        IncomingCatalogReadPage page = await reads.ReadAsync(fixture.Manager, new(new(Disposition: null)), CancellationToken.None);
        Assert.AreEqual(3, page.Summary.ProcessedToday);

        IncomingCatalogReadPage processed = await reads.ReadAsync(fixture.Manager,
            new(new(Disposition: null), Preset: IncomingCatalogPreset.ProcessedToday), CancellationToken.None);
        Assert.AreEqual(3, processed.Total);
        CollectionAssert.AreEquivalent(new[] { classifiedId, monitoredId, takenId },
            processed.Items.Select(item => item.Id).ToArray());
        Assert.IsFalse(processed.Items.Any(item => item.Id == viewedOnlyId));
    }

    [TestMethod]
    public async Task DuplicateDetectorExtractsCadastralAndPersistsManagerDecision()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Access, fixture.Workspace, TimeProvider.System);
        const string realDescription = """
            Продаём свой участок 6 соток (601 м²) в КП «Солнечный берег», д. Федюково.
            Адрес: Московская обл., Подольск г.о., д. Федюково, КП «Солнечный берег», земельный участок № 58.
            Кадастровый номер: 50:27:0020549:439.
            Категория земель: земли населённых пунктов. ВРИ: для индивидуального жилищного строительства (ИЖС).
            Стоимость: 6 500 000 ₽. Обременения отсутствуют. 700 м до станции.
            """;

        Guid firstId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Referral, "6 соток ИЖС в Федюково", "Федюково", 6_500_000m, 601m,
                null, null, null, realDescription, "Первый источник"), "duplicate-first", CancellationToken.None);
        CatalogItemDetail first = await fixture.Workspace.ReadItemAsync(fixture.Manager, firstId, CancellationToken.None);
        Assert.AreEqual("50:27:0020549:439", first.Item.CadastralNumber);

        string copied = realDescription.Replace("Кадастровый номер: 50:27:0020549:439.", "", StringComparison.Ordinal)
            .Replace("6 500 000", "6 450 000", StringComparison.Ordinal);
        Guid secondId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "6 COТOK ИЖC, Федюково", "д. Федюково", 6_450_000m, 600m,
                null, null, null, copied, "Второй источник"), "duplicate-second", CancellationToken.None);

        IncomingCatalogReadPage duplicates = await reads.ReadAsync(fixture.Manager,
            new(new(), Preset: IncomingCatalogPreset.PossibleDuplicate), CancellationToken.None);
        Assert.AreEqual(1, duplicates.Total);
        Assert.AreEqual(secondId, duplicates.Items.Single().Id);
        Assert.AreEqual(1, duplicates.Summary.PossibleDuplicate);

        IncomingCatalogDetailRead detail = await reads.ReadDetailAsync(fixture.Manager, secondId, CancellationToken.None);
        IncomingDuplicateCandidateView candidate = detail.DuplicateCandidates!.Single();
        Assert.AreEqual(firstId, candidate.CandidateListingId);
        Assert.IsTrue(candidate.Reasons.Any(reason => reason.Contains("описан", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(candidate.Reasons.Any(reason => reason.Contains("площад", StringComparison.OrdinalIgnoreCase)));

        await fixture.Workspace.ReviewDuplicateCandidateAsync(fixture.Manager,
            new(candidate.Id, candidate.Version, false), "duplicate-reject", CancellationToken.None);
        Assert.AreEqual(0, (await reads.ReadAsync(fixture.Manager,
            new(new(), Preset: IncomingCatalogPreset.PossibleDuplicate), CancellationToken.None)).Total);
        Assert.AreEqual(DuplicateCandidateStatus.Rejected, await ReadDuplicateStatusAsync(fixture, candidate.Id));
    }

    [TestMethod]
    public async Task ConfirmDuplicateMarksIncomingAsDuplicateAndAuditsDecision()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Access, fixture.Workspace, TimeProvider.System);
        const string cadastral = "50:27:0020549:439";

        await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Referral, "Участок Федюково", "Федюково", 6_500_000m, 601m,
                null, null, cadastral, "КП «Солнечный берег», участок № 58", "Первый источник"),
            "confirm-first", CancellationToken.None);
        Guid secondId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "Тот же участок", "Федюково", 6_450_000m, 601m,
                null, null, cadastral, "КП «Солнечный берег», участок № 58", "Второй источник"),
            "confirm-second", CancellationToken.None);

        IncomingCatalogDetailRead detail = await reads.ReadDetailAsync(fixture.Manager, secondId, CancellationToken.None);
        IncomingDuplicateCandidateView candidate = detail.DuplicateCandidates!.Single();
        await fixture.Workspace.ReviewDuplicateCandidateAsync(fixture.Manager,
            new(candidate.Id, candidate.Version, true), "duplicate-confirm", CancellationToken.None);

        CatalogItemDetail result = await fixture.Workspace.ReadItemAsync(fixture.Manager, secondId, CancellationToken.None);
        Assert.AreEqual(CatalogDisposition.Duplicate, result.Item.Disposition);
        Assert.AreEqual(DuplicateCandidateStatus.Confirmed, await ReadDuplicateStatusAsync(fixture, candidate.Id));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item =>
            item.ObjectId == secondId && item.Action == "CatalogDuplicateConfirmed")));
    }

    [TestMethod]
    public async Task DifferentExplicitPlotNumbersSuppressCopiedTemplateFalsePositive()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Access, fixture.Workspace, TimeProvider.System);
        string template = "КП «Солнечный берег», земельный участок № {0}. ИЖС, 601 м². "
            + "Электричество по границе, газификация, 700 м до станции. Собственник. "
            + "Тихая зелёная локация рядом с Москвой, инфраструктура в шаговой доступности.";

        await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Referral, "Федюково участок 58", "Федюково", 6_500_000m, 601m,
                null, null, null, template.Replace("{0}", "58", StringComparison.Ordinal), "Первый участок"), "plot-58", CancellationToken.None);
        await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "Федюково участок 59", "Федюково", 6_500_000m, 601m,
                null, null, null, template.Replace("{0}", "59", StringComparison.Ordinal), "Второй участок"), "plot-59", CancellationToken.None);

        Assert.AreEqual(0, (await reads.ReadAsync(fixture.Manager,
            new(new(), Preset: IncomingCatalogPreset.PossibleDuplicate), CancellationToken.None)).Total);
    }

    [TestMethod]
    public async Task PhotoFingerprintsDriveDuplicatesAndSettingsApplyWithoutRestart()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        IncomingCatalogReadService reads = new(fixture.Factory, fixture.Access, fixture.Workspace, TimeProvider.System);
        IncomingDuplicateMatchingMaintenance matcher = new(fixture.Factory, TimeProvider.System);
        DuplicateDetectionSettingsService settings = new(fixture.Factory, fixture.Access, TimeProvider.System);

        Guid firstId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Referral, "Северный участок", null, null, null,
                null, null, null, "Берёзовая роща, тупиковая дорога", "Фото-тест A"),
            "photo-duplicate-a", CancellationToken.None);
        Guid secondId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "Южный участок", null, null, null,
                null, null, null, "Открытое поле возле леса", "Фото-тест B"),
            "photo-duplicate-b", CancellationToken.None);

        await SeedFingerprintAsync(fixture, firstId, 0, unchecked((long)0x0F0F0F0F0F0F0F0FUL));
        await SeedFingerprintAsync(fixture, firstId, 1, unchecked((long)0x3333333333333333UL));
        await SeedFingerprintAsync(fixture, secondId, 0, unchecked((long)0x0F0F0F0F0F0F0F0EUL));
        await SeedFingerprintAsync(fixture, secondId, 1, unchecked((long)0x3333333333333331UL));

        await matcher.RefreshAsync(secondId, CancellationToken.None);
        IncomingCatalogReadPage duplicatePage = await reads.ReadAsync(fixture.Manager,
            new(new(), Preset: IncomingCatalogPreset.PossibleDuplicate), CancellationToken.None);
        Assert.AreEqual(1, duplicatePage.Total);
        IncomingCatalogDetailRead detail = await reads.ReadDetailAsync(fixture.Manager, secondId, CancellationToken.None);
        Assert.IsTrue(detail.DuplicateCandidates!.Single().Reasons.Any(reason =>
            reason.Contains("2 фотографии", StringComparison.Ordinal)));

        DuplicateDetectionSettingsView current = await settings.ReadAsync(fixture.Owner, CancellationToken.None);
        await settings.SaveAsync(fixture.Owner,
            new(current.CandidateThreshold, current.DescriptionSimilarityPercent, current.AreaTolerancePercent,
                0, current.StrongPhotoMatches, current.CommonPhotoMaxListings, current.Version),
            "photo-settings-strict", CancellationToken.None);

        await matcher.RefreshAsync(secondId, CancellationToken.None);
        Assert.AreEqual(0, (await reads.ReadAsync(fixture.Manager,
            new(new(), Preset: IncomingCatalogPreset.PossibleDuplicate), CancellationToken.None)).Total);
    }

    [TestMethod]
    public void PerceptualHashIsStableAcrossImageEncoding()
    {
        using SKBitmap bitmap = new(128, 96, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (SKCanvas canvas = new(bitmap))
        {
            canvas.Clear(new SKColor(231, 226, 205));
            using SKPaint dark = new() { Color = new SKColor(35, 74, 48), IsAntialias = true };
            using SKPaint light = new() { Color = new SKColor(198, 148, 65), IsAntialias = true };
            canvas.DrawRect(new SKRect(8, 10, 78, 82), dark);
            canvas.DrawCircle(92, 42, 27, light);
            canvas.DrawLine(12, 88, 118, 68, dark);
        }

        byte[] png = Encode(bitmap, SKEncodedImageFormat.Png, 100);
        byte[] jpeg = Encode(bitmap, SKEncodedImageFormat.Jpeg, 82);
        long pngHash = PhotoFingerprintWorker.PerceptualHash(png);
        long jpegHash = PhotoFingerprintWorker.PerceptualHash(jpeg);
        int distance = System.Numerics.BitOperations.PopCount(unchecked((ulong)(pngHash ^ jpegHash)));

        Assert.IsTrue(distance <= 8, $"pHash distance after ordinary re-encoding was {distance}.");
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(format, quality);
        return data.ToArray();
    }

    private static async Task SeedFingerprintAsync(
        ProcurementTests.Phase1Fixture fixture, Guid listingId, int index, long hash)
    {
        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        Listing listing = await db.Listings.AsNoTracking().SingleAsync(item => item.Id == listingId);
        db.CatalogPhotoFingerprints.Add(new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = listing.OrganizationId,
            ListingId = listingId,
            PhotoIndex = index,
            UrlHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{listingId}:{index}"))),
            PerceptualHash = hash,
            Status = PhotoFingerprintStatus.Ready,
            SourceDataRevision = listing.DataRevision,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<DuplicateCandidateStatus> ReadDuplicateStatusAsync(ProcurementTests.Phase1Fixture fixture, Guid id)
    {
        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        return await db.CatalogDuplicateCandidates.Where(item => item.Id == id).Select(item => item.Status).SingleAsync();
    }

    private static async Task DecideAsync(ProcurementTests.Phase1Fixture fixture, Guid caseId,
        ProcurementAction action, string reason)
    {
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, caseId, CancellationToken.None);
        await fixture.Workspace.DecideAsync(fixture.Manager,
            new(caseId, card.Item.CaseVersion, card.Item.SourceRevision, action, reason, "", null, null),
            "case-lifecycle", CancellationToken.None);
    }
}
