using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ReleasePackageB303Tests
{
    [TestMethod]
    public async Task ServerValidatesInspectionAnswersAgainstSnapshotSemantics()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b303-take", CancellationToken.None);
        Guid inspectionId = await fixture.Workspace.SaveInspectionAsync(fixture.Manager,
            new(taken.CaseId, null, null, "", "", [], false), "b303-start", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        InspectionView inspection = card.Inspection!;

        InspectionItemView choice = inspection.Items.First(item => item.AnswerType == InspectionAnswerType.Choice);
        InspectionItemView boolean = inspection.Items.First(item => item.AnswerType == InspectionAnswerType.Boolean);
        InspectionItemView number = inspection.Items.First(item => item.AnswerType == InspectionAnswerType.Number);
        InspectionItemView percentage = inspection.Items.First(item => item.AnswerType == InspectionAnswerType.Percentage);

        await AssertInvalidAsync(fixture, taken.CaseId, inspection, choice, "Значение вне snapshot");
        await AssertInvalidAsync(fixture, taken.CaseId, inspection, boolean, "иногда");
        await AssertInvalidAsync(fixture, taken.CaseId, inspection, number, "двенадцать");
        await AssertInvalidAsync(fixture, taken.CaseId, inspection, percentage, "101");

        InspectionAnswer[] valid =
        [
            new(choice.Id, choice.Version, InspectionItemStatus.Answered, choice.Options[0], ""),
            new(boolean.Id, boolean.Version, InspectionItemStatus.Answered, "TRUE", ""),
            new(number.Id, number.Version, InspectionItemStatus.Answered, "12,5", ""),
            new(percentage.Id, percentage.Version, InspectionItemStatus.Answered, "37,5", "")
        ];
        await fixture.Workspace.SaveInspectionAsync(fixture.Manager,
            new(taken.CaseId, inspectionId, inspection.Version, "Черновик B3-03", "", valid, false),
            "b303-valid", CancellationToken.None);

        CaseCard saved = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(choice.Options[0], saved.Inspection!.Items.Single(item => item.Id == choice.Id).Answer);
        Assert.AreEqual("true", saved.Inspection.Items.Single(item => item.Id == boolean.Id).Answer);
        Assert.AreEqual("12.5", saved.Inspection.Items.Single(item => item.Id == number.Id).Answer);
        Assert.AreEqual("37.5", saved.Inspection.Items.Single(item => item.Id == percentage.Id).Answer);
    }

    [TestMethod]
    public async Task StaleInspectionVersionCannotSilentlyOverwriteNewerDraft()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b303-take", CancellationToken.None);
        Guid inspectionId = await fixture.Workspace.SaveInspectionAsync(fixture.Manager,
            new(taken.CaseId, null, null, "", "", [], false), "b303-start", CancellationToken.None);
        CaseCard first = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        InspectionView stale = first.Inspection!;

        await fixture.Workspace.SaveInspectionAsync(fixture.Manager,
            new(taken.CaseId, inspectionId, stale.Version, "Новая серверная версия", "",
                stale.Items.Select(item => new InspectionAnswer(item.Id, item.Version, InspectionItemStatus.NotChecked, "", "")).ToArray(), false),
            "b303-first-save", CancellationToken.None);

        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => fixture.Workspace.SaveInspectionAsync(fixture.Manager,
            new(taken.CaseId, inspectionId, stale.Version, "Устаревший локальный черновик", "",
                stale.Items.Select(item => new InspectionAnswer(item.Id, item.Version, InspectionItemStatus.NotChecked, "", "")).ToArray(), false),
            "b303-stale-save", CancellationToken.None));

        CaseCard current = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual("Новая серверная версия", current.Inspection!.OverallConclusion);
    }

    private static async Task AssertInvalidAsync(ProcurementTests.Phase1Fixture fixture, Guid caseId,
        InspectionView inspection, InspectionItemView item, string value)
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.SaveInspectionAsync(fixture.Manager,
            new(caseId, inspection.Id, inspection.Version, "", "",
                [new InspectionAnswer(item.Id, item.Version, InspectionItemStatus.Answered, value, "")], false),
            "b303-invalid", CancellationToken.None));
    }
}
