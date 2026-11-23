using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Server.Components.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class ParsingRetryStateTests
{
    internal static SearchView Search(bool enabled = true, CollectionRunView? run = null) => new(Guid.NewGuid(), "Тест", CatalogSource.Avito,
        "https://www.avito.ru/moskva/zemelnye_uchastki", 10, null, "Без группы", "Вручную", CollectionScheduleKind.Manual,
        null, [], enabled, null, 1, null, run);
    internal static CollectionRunView Run(string state) => new(Guid.NewGuid(), null, state, DateTimeOffset.UtcNow,
        null, null, "", 0, 0, 0, 0, "", [], null, 0, null, false);

    [TestMethod]
    public void PauseAndActiveWorkExplainWhyTheButtonIsDisabled()
    {
        var pause = CollectionRunAction.For(Search(false), CollectionEnqueueStage.None, true, false, true);
        Assert.AreEqual("На паузе", pause.Label); Assert.IsTrue(pause.Disabled);
        StringAssert.Contains(pause.Help, "Поиск активен");
        foreach (string state in new[] { "Pending", "Leased" })
        {
            var action = CollectionRunAction.For(Search(run: Run(state)), CollectionEnqueueStage.None, true, false, true);
            Assert.AreEqual(state == "Pending" ? "В очереди" : "Выполняется", action.Label); Assert.IsTrue(action.Disabled);
        }
    }
    [TestMethod]
    public void ConfirmedAndUncertainRepliesNeverOfferBlindResubmission()
    {
        var search = Search();
        foreach (var (stage, label) in new[] { (CollectionEnqueueStage.Sending, "Отправляем…"),
            (CollectionEnqueueStage.Confirmed, "В очереди"), (CollectionEnqueueStage.Uncertain, "Не подтверждено") })
        {
            var action = CollectionRunAction.For(search, stage, true, false, true);
            Assert.AreEqual(label, action.Label); Assert.IsTrue(action.Disabled);
        }
        var running = CollectionRunAction.For(search with { ActiveRun = Run("Leased") }, CollectionEnqueueStage.Confirmed, true, false, true);
        Assert.AreEqual("Выполняется", running.Label);
        Assert.IsFalse(CollectionRunAction.For(Search(), CollectionEnqueueStage.None, true, false, true).Disabled,
            "Another search has no shared enqueue-busy state.");
        Assert.IsTrue(CollectionRunAction.For(Search(), CollectionEnqueueStage.None, false, false, true).Disabled);
    }
}
