using LandErp.Server.Components.Pages;
using LandErp.Collector.Contracts.V1;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class ParsingWorkspaceTests
{
    [TestMethod]
    [DataRow("AREA_GEOMETRY_CHANGED", "Контур изменился", "area")]
    [DataRow("AREA_FILTERS_CHANGED", "Условия поиска изменились", "area")]
    [DataRow("AREA_GEOMETRY_UNAVAILABLE", "Область не подтверждена", "area")]
    [DataRow("AREA_SELECTION_UNCONFIRMED", "Область не подтверждена", "area")]
    [DataRow("AREA_COORDINATES_MISSING", "Не хватает координат", "collection")]
    public void ExplicitAreaEvidenceDeterminesCause(string code, string label, string category)
    {
        var result = CollectionResultPresentation.From("Partial", "Partial", CollectionResultReasonCodes.EndNotConfirmed, [code], false);
        Assert.AreEqual(label, result.Label); Assert.AreEqual(category, result.Category); Assert.IsTrue(result.IsIssue);
    }

    [TestMethod]
    public void PartialAndUnknownCodesNeverInventLostAreaOrExposeProviderText()
    {
        var partial = CollectionResultPresentation.From("Partial", "Partial", CollectionResultReasonCodes.EndNotConfirmed, [], false);
        Assert.AreEqual("Неполный сбор", partial.Label); Assert.AreEqual("collection", partial.Category); Assert.IsTrue(partial.IsIssue);
        var unknown = CollectionResultPresentation.From("Failed", "SourceError", "private-provider-text", ["<script>"], true);
        StringAssert.Contains(unknown.Help, "Точная причина не передана");
        Assert.IsFalse(unknown.Help.Contains("private-provider-text", StringComparison.Ordinal));
        Assert.IsFalse(unknown.Help.Contains("<script>", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AccessEvidenceTakesPriorityAndDoesNotClaimPermanentBan()
    {
        foreach(var code in new[]{"Captcha", "AuthenticationRequired", "RateLimited"})
        {
            var result = CollectionResultPresentation.From("Leased", code, "", ["AREA_GEOMETRY_UNAVAILABLE"], false);
            Assert.AreEqual("access", result.Category); Assert.IsTrue(result.IsIssue);
            Assert.IsFalse(result.Label.Contains("бан", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void NoRunActiveRunSuccessAndConfiguredLimitAreDistinct()
    {
        Assert.AreEqual("Ещё не запускался", CollectionResultPresentation.From((LandErp.Application.Modules.Collection.Contracts.CollectionRunView?)null).Label);
        foreach(string state in new[]{"Pending", "Leased", "Completed", "LimitReached"})
            Assert.IsFalse(CollectionResultPresentation.From(state, "", "", [], false).IsIssue);
        Assert.AreEqual("В очереди", CollectionResultPresentation.From("Pending", "", "", [], false).Label);
        Assert.AreEqual("Достигнут лимит страниц", CollectionResultPresentation.From("LimitReached", "LimitReached", "PAGE_LIMIT_REACHED", [], false).Label);
        Assert.IsFalse(CollectionResultPresentation.From("Completed", "Success", "", ["AREA_SELECTION_UNCONFIRMED"], false).IsIssue,
            "Transient loading warnings must not turn a successful final result into an issue.");
    }
}
