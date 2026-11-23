using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LandErp.Server.Components.Pages;

public partial class Collectors
{
    private string activeTab = "home", issueFilter = "all", sourceFilter = "all";
    private string zoneQuery = "", zoneState = "active", historySource = "all";
    private bool saveAndRun;
    private readonly ElementReference[] tabButtons = new ElementReference[4];
    private static readonly string[] TabKeys = ["home", "zones", "searches", "history"];
    private static readonly string[] TabLabels = ["Главная", "Зоны поиска", "Список поисков", "История запусков"];
    private IEnumerable<int> VisibleTabs => view?.CanReadCollection == true ? [0, 1, 2, 3] : [1];
    private bool CanShowWorkspace => !Loading && !Forbidden && view is not null;
    private async Task SelectTab(string tab)
    {
        activeTab = tab;
        if (tab == "history" && !historyInitialized) await LoadHistoryAsync();
    }
    private async Task TabKeyAsync(KeyboardEventArgs e, int index)
    {
        int[] tabs = VisibleTabs.ToArray();
        int position = Array.IndexOf(tabs, index);
        int target = e.Key switch { "ArrowRight" => (position + 1) % tabs.Length, "ArrowLeft" => (position + tabs.Length - 1) % tabs.Length,
            "Home" => 0, "End" => tabs.Length - 1, _ => -1 };
        if (target < 0) return;
        await SelectTab(TabKeys[tabs[target]]);
        await tabButtons[tabs[target]].FocusAsync();
    }
    private static CollectionResultPresentation Result(CollectionRunView? run) => CollectionResultPresentation.From(run);
    private static CollectionResultPresentation Result(CollectionJobView job) => CollectionResultPresentation.From(job);
    private static CollectionRunView? IssueRun(SearchView search) => search.ActiveRun is { } active && Result(active).IsIssue ? active : search.LastRun;
    private static bool HasIssue(SearchView search) => Result(IssueRun(search)).IsIssue;
    private bool IsArchivedSearch(SearchView search) => search.GroupId is { } id && view?.Groups.Any(g => g.Id == id && !g.Active) == true;
    private SearchView[] IssueSearches => view?.Searches.Where(s => !IsArchivedSearch(s) && HasIssue(s)).OrderBy(s => Result(IssueRun(s)).Category == "area" ? 0 : 1)
        .ThenByDescending(s => IssueRun(s)?.CompletedAt ?? IssueRun(s)?.CreatedAt).ToArray() ?? [];
    private IEnumerable<SearchView> VisibleIssues => IssueSearches.Where(s => issueFilter == "all" || Result(IssueRun(s)).Category == issueFilter);
    private int IssueCount(string category) => IssueSearches.Count(s => category == "all" || Result(IssueRun(s)).Category == category);
    private static string IssueFilterLabel(string key) => key switch { "area" => "Область", "access" => "Доступ", "collection" => "Сбор", _ => "Все" };
    private static string DisplayName(SearchView search) => search.GroupId.HasValue ? search.Group : search.Label;
    private static bool Active(SearchView search) => search.ActiveRun is not null;
    private bool CanRun(SearchView search) => !RunAction(search).Disabled;
    private bool CanEdit(SearchView search) => CanManageCollection && !Busy && databaseReady && !Active(search)
        && (!enqueueStates.TryGetValue(search.Id, out var state) || state.Stage == CollectionEnqueueStage.Rejected);
    private SearchView? FindSearch(Guid id) => view?.Searches.FirstOrDefault(s => s.Id == id);
    private SearchView[] WorkspaceSearches => view?.Searches.Where(s => Matches(s)
        && (sourceFilter == "all" || s.Source.ToString() == sourceFilter)
        && (s.GroupId is null ? groupVisibility != "archived" : view.Groups.Any(g => g.Id == s.GroupId &&
            (groupVisibility == "all" || (groupVisibility == "archived" ? !g.Active : g.Active)))))
        .OrderBy(s => s.Group).ThenBy(s => s.Label).ThenBy(s => s.Source).ToArray() ?? [];
    private SearchGroupView[] WorkspaceZones => view?.Groups.Where(g =>
        (zoneQuery.Length == 0 || g.Name.Contains(zoneQuery, StringComparison.OrdinalIgnoreCase)) &&
        (zoneState == "all" || (zoneState == "archived" ? !g.Active : g.Active)))
        .OrderBy(g => g.SortOrder).ThenBy(g => g.Name).ToArray() ?? [];
    private SearchView[] ZoneSearches(Guid id) => view?.Searches.Where(s => s.GroupId == id).ToArray() ?? [];
    private int ZoneIssueCount(Guid id) => ZoneSearches(id).Count(HasIssue);
    private async Task ShowZoneSearches(Guid id)
    {
        ResetFilters(); sourceFilter = "all"; groupVisibility = "all"; groupFilter = id.ToString(); ResetListPosition("searches");
        await SelectTab("searches");
    }
    private static bool Finished(string state) => state is not ("Pending" or "Leased");
    private static string SavedCount(CollectionRunView? run) => run is null ? "Сохранено —" : Finished(run.State) ? $"Сохранено {run.AcceptedCount} объявлений" : "Результат ожидается";
    private async Task OpenLinkEdit(SearchView search)
    {
        if (!CanEdit(search)) return;
        await OpenEditSearch(search);
        saveAndRun = true;
    }
}
