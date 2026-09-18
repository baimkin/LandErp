using System.Text.Json;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ReleasePackageB201Tests
{
    [TestMethod]
    public async Task CorrectionsCoverAllWorkingFactsAndDoNotMutateCatalogSource()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid catalogId = await fixture.Workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "Исходное название", "Химки", 19_000_000m, 1500m, null, null,
                "50:10:0000000:1", null, "B2-01"), "b201-source", CancellationToken.None);
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(catalogId), "b201-take", CancellationToken.None);

        Listing beforeSource;
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
            beforeSource = await db.Listings.AsNoTracking().SingleAsync(item => item.Id == catalogId);

        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        card = await CorrectAsync(fixture, card, CaseFactField.Title, "Исправленное название", null, "Исправлена опечатка");
        card = await CorrectAsync(fixture, card, CaseFactField.Price, null, 18_500_000m, "Уточнена рабочая цена");
        card = await CorrectAsync(fixture, card, CaseFactField.AreaSquareMeters, null, 1473.25m, "Уточнена площадь");
        card = await CorrectAsync(fixture, card, CaseFactField.Location, "Фирсановка", null, "Уточнена локация");
        await CorrectAsync(fixture, card, CaseFactField.CadastralNumber, "50:10:0000000:2", null, "Исправлен кадастровый номер");

        PropertyCase corrected = await fixture.ReadCaseAsync(taken.CaseId);
        Assert.AreEqual("Исправленное название", corrected.WorkingTitle);
        Assert.AreEqual(18_500_000m, corrected.WorkingPrice);
        Assert.AreEqual(1473.25m, corrected.WorkingAreaSquareMeters);
        Assert.AreEqual("Фирсановка", corrected.WorkingLocation);
        Assert.AreEqual("50:10:0000000:2", corrected.CadastralNumber);

        Listing afterSource;
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
            afterSource = await db.Listings.AsNoTracking().SingleAsync(item => item.Id == catalogId);
        Assert.AreEqual(beforeSource.Title, afterSource.Title);
        Assert.AreEqual(beforeSource.Price, afterSource.Price);
        Assert.AreEqual(beforeSource.AreaSquareMeters, afterSource.AreaSquareMeters);
        Assert.AreEqual(beforeSource.Location, afterSource.Location);
        Assert.AreEqual(beforeSource.CadastralNumber, afterSource.CadastralNumber);

        CaseCard result = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        TimelineItem cadastral = result.Timeline.First(item => item.Kind == "FactCorrected" && item.Body.Contains("Кадастровый номер", StringComparison.Ordinal));
        StringAssert.Contains(cadastral.Body, "50:10:0000000:1 → 50:10:0000000:2");
        StringAssert.Contains(cadastral.Body, "Причина: Исправлен кадастровый номер");
        Assert.IsFalse(string.IsNullOrWhiteSpace(cadastral.Actor));
        Assert.AreNotEqual(default, cadastral.RecordedAt);

        await using LandErpDbContext auditDb = await fixture.Factory.CreateDbContextAsync();
        var audits = await auditDb.AuditEvents.AsNoTracking()
            .Where(item => item.ObjectId == taken.CaseId && item.Action == "PropertyCaseFactCorrected")
            .OrderBy(item => item.RecordedAt).ToArrayAsync();
        Assert.AreEqual(5, audits.Length);
        Assert.AreEqual(fixture.Manager.UserId, audits[^1].ActorId);
        using JsonDocument changes = JsonDocument.Parse(audits[^1].Changes);
        Assert.AreEqual("CadastralNumber", changes.RootElement.GetProperty("Field").GetString());
        Assert.AreEqual("50:10:0000000:1", changes.RootElement.GetProperty("Before").GetString());
        Assert.AreEqual("50:10:0000000:2", changes.RootElement.GetProperty("After").GetString());
        Assert.AreEqual("Исправлен кадастровый номер", changes.RootElement.GetProperty("Reason").GetString());
    }

    [TestMethod]
    public async Task StaleCaseVersionCannotOverwriteCorrection()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("Версионный объект", "Москва", null, 4_000_000m, 900m, "B2-01"), "b201-create", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);

        await fixture.Workspace.CorrectCaseFactAsync(fixture.Manager,
            new(created.CaseId, card.Item.CaseVersion, CaseFactField.Price, null, 3_900_000m, "Первая корректировка"),
            "b201-first", CancellationToken.None);

        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => fixture.Workspace.CorrectCaseFactAsync(fixture.Manager,
            new(created.CaseId, card.Item.CaseVersion, CaseFactField.Price, null, 3_800_000m, "Старая версия"),
            "b201-stale", CancellationToken.None));

        PropertyCase value = await fixture.ReadCaseAsync(created.CaseId);
        Assert.AreEqual(3_900_000m, value.WorkingPrice);
    }

    [TestMethod]
    public async Task CorrectionRequiresReasonAndServerAccess()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("Объект с правами", "Москва", null, null, null, "B2-01"), "b201-create", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.CorrectCaseFactAsync(fixture.Manager,
            new(created.CaseId, card.Item.CaseVersion, CaseFactField.Location, "Химки", null, " "),
            "b201-no-reason", CancellationToken.None));

        Subject unknown = new(Guid.CreateVersion7(), false);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.CorrectCaseFactAsync(unknown,
            new(created.CaseId, card.Item.CaseVersion, CaseFactField.Location, "Химки", null, "Уточнение адреса"),
            "b201-denied", CancellationToken.None));
    }

    [TestMethod]
    public async Task OptionalWorkingFactCanBeClearedWithAudit()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("Объект для очистки", "Ошибочная локация", "50:10:0000000:9", 4_000_000m, 900m, "B2-01"),
            "b201-create", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);

        await fixture.Workspace.CorrectCaseFactAsync(fixture.Manager,
            new(created.CaseId, card.Item.CaseVersion, CaseFactField.Location, null, null, "Локация указана ошибочно"),
            "b201-clear", CancellationToken.None);

        PropertyCase value = await fixture.ReadCaseAsync(created.CaseId);
        Assert.IsNull(value.WorkingLocation);
        CaseCard result = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        TimelineItem correction = result.Timeline.First(item => item.Kind == "FactCorrected");
        StringAssert.Contains(correction.Body, "Ошибочная локация → —");
    }

    private static async Task<CaseCard> CorrectAsync(ProcurementTests.Phase1Fixture fixture, CaseCard card,
        CaseFactField field, string? text, decimal? number, string reason)
    {
        await fixture.Workspace.CorrectCaseFactAsync(fixture.Manager,
            new(card.Item.CaseId, card.Item.CaseVersion, field, text, number, reason),
            "b201-" + field, CancellationToken.None);
        return await fixture.Workspace.ReadCardAsync(fixture.Manager, card.Item.CaseId, CancellationToken.None);
    }
}
