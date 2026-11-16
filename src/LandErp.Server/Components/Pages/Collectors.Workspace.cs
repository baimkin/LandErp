using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace LandErp.Server.Components.Pages;

public partial class Collectors
{
    private string activeTab = "home", issueFilter = "all", sourceFilter = "all";
    private string zoneQuery = "", zoneState = "active", historySource = "all";
    private int searchPage = 1, zonePage = 1;
    private bool saveAndRun;
    private readonly ElementReference[] tabButtons = new ElementReference[4];
    private static readonly string[] TabKeys = ["home", "zones", "searches", "history"];
    private static readonly string[] TabLabels = ["Главная", "Зоны поиска", "Список поисков", "История запусков"];
    private IEnumerable<int> VisibleTabs => view?.CanReadCollection == true ? [0, 1, 2, 3] : [1];
    private bool CanShowWorkspace => !Loading && !Forbidden && view is not null;
    private void SelectTab(string tab) { activeTab = tab; }
    private async Task TabKeyAsync(KeyboardEventArgs e, int index)
    {
        int[] tabs = VisibleTabs.ToArray();
        int position = Array.IndexOf(tabs, index);
        int target = e.Key switch { "ArrowRight" => (position + 1) % tabs.Length, "ArrowLeft" => (position + tabs.Length - 1) % tabs.Length,
            "Home" => 0, "End" => tabs.Length - 1, _ => -1 };
        if (target < 0) return;
        SelectTab(TabKeys[tabs[target]]);
        await tabButtons[tabs[target]].FocusAsync();
    }
    private static CollectionResultPresentation Result(CollectionRunView? run) => CollectionResultPresentation.From(run);
    private static CollectionResultPresentation Result(CollectionJobView job) => CollectionResultPresentation.From(job);
    private static CollectionRunView? IssueRun(SearchView search) => search.ActiveRun is { } active && Result(active).IsIssue ? active : search.LastRun;
    private static bool HasIssue(SearchView search) => Result(IssueRun(search)).IsIssue;
    private SearchView[] IssueSearches => view?.Searches.Where(HasIssue).OrderBy(s => Result(IssueRun(s)).Category == "area" ? 0 : 1)
        .ThenByDescending(s => IssueRun(s)?.CompletedAt ?? IssueRun(s)?.CreatedAt).ToArray() ?? [];
    private IEnumerable<SearchView> VisibleIssues => IssueSearches.Where(s => issueFilter == "all" || Result(IssueRun(s)).Category == issueFilter);
    private int IssueCount(string category) => IssueSearches.Count(s => category == "all" || Result(IssueRun(s)).Category == category);
    private static string IssueFilterLabel(string key) => key switch { "area" => "Область", "access" => "Доступ", "collection" => "Сбор", _ => "Все" };
    private static string DisplayName(SearchView search) => search.GroupId.HasValue ? search.Group : search.Label;
    private static bool Active(SearchView search) => search.ActiveRun is not null;
    private bool CanRun(SearchView search) => CanManageCollection && !Busy && databaseReady && search.Enabled && !Active(search);
    private bool CanEdit(SearchView search) => CanManageCollection && !Busy && databaseReady && !Active(search);
    private static string RunDisabledReason(SearchView search) => Active(search) ? "Поиск уже в очереди или выполняется" : !search.Enabled ? "Сначала включите поиск в настройках" : "Запустить сейчас";
    private SearchView? FindSearch(Guid id) => view?.Searches.FirstOrDefault(s => s.Id == id);
    private SearchView[] WorkspaceSearches => view?.Searches.Where(s => Matches(s)
        && (sourceFilter == "all" || s.Source.ToString() == sourceFilter)
        && (s.GroupId is null ? groupVisibility != "archived" : view.Groups.Any(g => g.Id == s.GroupId &&
            (groupVisibility == "all" || (groupVisibility == "archived" ? !g.Active : g.Active)))))
        .OrderBy(s => s.Group).ThenBy(s => s.Label).ThenBy(s => s.Source).ToArray() ?? [];
    private int SearchPages => Pages(WorkspaceSearches.Length);
    private int CurrentSearchPage => Math.Clamp(searchPage, 1, SearchPages);
    private IEnumerable<SearchView> PagedSearches => WorkspaceSearches.Skip((CurrentSearchPage - 1) * PageSize).Take(PageSize);
    private SearchGroupView[] WorkspaceZones => view?.Groups.Where(g =>
        (zoneQuery.Length == 0 || g.Name.Contains(zoneQuery, StringComparison.OrdinalIgnoreCase)) &&
        (zoneState == "all" || (zoneState == "archived" ? !g.Active : g.Active)))
        .OrderBy(g => g.SortOrder).ThenBy(g => g.Name).ToArray() ?? [];
    private int ZonePages => Pages(WorkspaceZones.Length);
    private int CurrentZonePage => Math.Clamp(zonePage, 1, ZonePages);
    private IEnumerable<SearchGroupView> PagedZones => WorkspaceZones.Skip((CurrentZonePage - 1) * PageSize).Take(PageSize);
    private SearchView[] ZoneSearches(Guid id) => view?.Searches.Where(s => s.GroupId == id).ToArray() ?? [];
    private int ZoneIssueCount(Guid id) => ZoneSearches(id).Count(HasIssue);
    private void ShowZoneSearches(Guid id)
    {
        ResetFilters(); sourceFilter = "all"; groupVisibility = "all"; groupFilter = id.ToString(); searchPage = 1;
        SelectTab("searches");
    }
    private static int Pages(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    private static string PageRange(int page, int count) => count == 0 ? "0" : $"{(page - 1) * PageSize + 1}–{Math.Min(page * PageSize, count)} из {count}";
    private static bool Finished(string state) => state is not ("Pending" or "Leased");
    private static string SavedCount(CollectionRunView? run) => run is null ? "Сохранено —" : Finished(run.State) ? $"Сохранено {run.AcceptedCount} объявлений" : "Результат ожидается";
    private async Task OpenLinkEdit(SearchView search)
    {
        if (!CanEdit(search)) return;
        await OpenEditSearch(search);
        saveAndRun = true;
    }
}
