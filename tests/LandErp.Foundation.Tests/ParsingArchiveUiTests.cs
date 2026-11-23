using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Server.Components.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class ParsingArchiveUiTests
{
    [TestMethod]
    public async Task ActualPageExcludesArchivedIssuesAndCountsButKeepsArchiveInSearchList()
    {
        Guid archivedGroup = Guid.NewGuid(), activeGroup = Guid.NewGuid();
        var archived = ParsingRetryStateTests.Search() with { Label = "Старые Дубки", GroupId = archivedGroup, Group = "Старые Дубки",
            Enabled = true, LastRun = ParsingRetryStateTests.Run("Failed") };
        var active = ParsingRetryStateTests.Search() with { Label = "Дубки 2", GroupId = activeGroup, Group = "Дубки 2",
            LastRun = ParsingRetryStateTests.Run("Failed") };
        var ungrouped = ParsingRetryStateTests.Search() with { Label = "Без группы", LastRun = ParsingRetryStateTests.Run("Failed") };
        SearchGroupView[] groups = [new(archivedGroup, "Старые Дубки", 0, false, 1, 1), new(activeGroup, "Дубки 2", 1, true, 1, 1)];
        int calls = 0;
        var admin = ParsingRetryComponentTests.Stub<ICollectionAdministration>((_, _) => { calls++; return Task.CompletedTask; });
        await using var f = new ParsingRetryComponentTests.ComponentFixture(admin, [archived, active, ungrouped], groups, "home");
        await f.RenderAsync();
        await f.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            string html = System.Net.WebUtility.HtmlDecode(f.View.ToHtmlString());
            int start = html.IndexOf("<table class=\"workspace-table issue-table\"", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0);
            string issueTable = html[start..html.IndexOf("</table>", start, StringComparison.Ordinal)];
            Assert.IsFalse(issueTable.Contains(archived.Id.ToString(), StringComparison.Ordinal));
            StringAssert.Contains(issueTable, active.Id.ToString()); StringAssert.Contains(issueTable, ungrouped.Id.ToString());
            int headingStart = html.IndexOf("Требуют внимания ", StringComparison.Ordinal);
            string heading = html[headingStart..html.IndexOf("</h2>", headingStart, StringComparison.Ordinal)];
            Assert.IsTrue(heading.Contains(">2</span>", StringComparison.Ordinal), "Issue heading counts only the active and ungrouped searches.");
            int categoryStart = html.IndexOf(">Все <span", StringComparison.Ordinal);
            string category = html[categoryStart..html.IndexOf("</button>", categoryStart, StringComparison.Ordinal)];
            Assert.IsTrue(category.Contains(">2</span>", StringComparison.Ordinal), "Issue category excludes the archive too.");
            var action = f.Page!.Action(archived);
            Assert.AreEqual("В архиве", action.Label); Assert.IsTrue(action.Disabled);
            StringAssert.Contains(action.Help, "Зона в архиве");
            await f.Page.Enqueue(archived.Id); Assert.AreEqual(0, calls);
        });
        await using var list = new ParsingRetryComponentTests.ComponentFixture(admin, [archived, active, ungrouped], groups, includeArchive: true);
        await list.RenderAsync();
        await list.Renderer.Dispatcher.InvokeAsync(() =>
        {
            // Archives stay accessible explicitly; excluding the main issues never deletes their old result.
            string html = System.Net.WebUtility.HtmlDecode(list.View.ToHtmlString());
            int start = html.IndexOf("<table class=\"workspace-table search-table\"", StringComparison.Ordinal);
            string table = html[start..html.IndexOf("</table>", start, StringComparison.Ordinal)];
            StringAssert.Contains(table, archived.Id.ToString());
            StringAssert.Contains(table, "В архиве");
            StringAssert.Contains(table, "Ошибка сбора");
        });
    }
    [TestMethod]
    public void ArchiveBlocksEvenAnEnabledSearchAndKeepsOtherSearchesAvailable()
    {
        var search = ParsingRetryStateTests.Search();
        var archived = CollectionRunAction.For(search, CollectionEnqueueStage.None, true, false, true, archived: true);
        Assert.AreEqual("В архиве", archived.Label); Assert.IsTrue(archived.Disabled);
        Assert.IsFalse(CollectionRunAction.For(search, CollectionEnqueueStage.None, true, false, true).Disabled);
    }
}
