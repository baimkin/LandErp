using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LandErp.Server.Components.Pages;

public partial class Collectors
{
    [Inject] private IJSRuntime ListsJs { get; set; } = default!;
    private ElementReference listsRoot;
    private IJSObjectReference? listsModule;
    private bool listsStarted, listsMounted;
    private readonly HashSet<string> listPositionResets = [];
    private List<CollectionJobView> historyRows = [];
    private CollectionHistoryCursor? historyNext;
    private int historyTotal, historyRequestedCount = 50, historyVersion;
    private bool historyInitialized, historyLoading;
    private string? historyError;
    private CancellationTokenSource? historyReadCancellation;

    private void ResetListPosition(string tab) => listPositionResets.Add(tab);
    private async Task HistoryFiltersChangedAsync()
    {
        historyQuery = historyQuery.Length > 200 ? historyQuery[..200] : historyQuery;
        historyRequestedCount = 50;
        historyRows = [];
        historyNext = null;
        ResetListPosition("history");
        await LoadHistoryAsync();
    }
    private CollectionHistoryQuery HistoryQuery(CollectionHistoryCursor? before, int size) => new(historyQuery,
        Enum.TryParse<CatalogSource>(historySource, out var source) ? source : null,
        historyFilter switch { "completed" => CollectionHistoryResult.Completed, "attention" => CollectionHistoryResult.Attention,
            "running" => CollectionHistoryResult.Running, _ => CollectionHistoryResult.All }, before, size);

    private async Task LoadHistoryAsync(bool append = false)
    {
        if (append && (historyLoading || historyNext is null) || view?.CanReadCollection != true) return;
        int version = ++historyVersion;
        historyReadCancellation?.Cancel();
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        historyReadCancellation = readCancellation;
        historyLoading = true;
        historyError = null;
        historyInitialized = true;
        try
        {
            var items = new List<CollectionJobView>();
            CollectionHistoryCursor? cursor = append ? historyNext : null;
            int remaining = append ? 50 : historyRequestedCount;
            int total = 0;
            // Refresh the loaded prefix atomically. Normal continuation always requests exactly 50.
            // Batched reads also support a long loaded window without an unbounded single request.
            do
            {
                var page = await Administration.ReadHistoryAsync(CurrentSubject, HistoryQuery(cursor, Math.Min(1000, remaining)), readCancellation.Token);
                if (version != historyVersion) return;
                items.AddRange(page.Items);
                total = page.Total;
                cursor = page.Next;
                remaining -= page.Items.Count;
            } while (!append && remaining > 0 && cursor is not null);
            if (version != historyVersion) return;
            historyRows = append ? historyRows.Concat(items).DistinctBy(j => j.Id).ToList() : items;
            historyNext = cursor;
            historyTotal = total;
            if (append) historyRequestedCount += 50;
        }
        catch (OperationCanceledException) when (readCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (version != historyVersion) return;
            if (exception is AccessDeniedException) { historyRows = []; historyNext = null; }
            historyError = ReadFailure(exception, "Не удалось загрузить историю. Сохранённые строки и фильтры оставлены на месте.");
        }
        finally
        {
            if (ReferenceEquals(historyReadCancellation, readCancellation)) historyReadCancellation = null;
            if (version == historyVersion) historyLoading = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!CanShowWorkspace) return;
        try
        {
            if (!listsStarted)
            {
                listsStarted = true;
                listsModule = await ListsJs.InvokeAsync<IJSObjectReference>("import", "./js/parsing-lists.js");
                string key = $"landerp.parsing-lists.v1:{effectiveAccess!.OrganizationId}:{effectiveAccess.EmployeeId}";
                var saved = await listsModule.InvokeAsync<ParsingListPreferences?>("loadPreferences", key);
                if (saved is not null) ApplyListPreferences(saved);
                await listsModule.InvokeVoidAsync("mount", listsRoot, key);
                listsMounted = true;
                if (activeTab == "history" && !historyInitialized) await LoadHistoryAsync();
                StateHasChanged();
                return;
            }
            if (listsModule is not null && listsMounted)
            {
                await listsModule.InvokeVoidAsync("update", listsRoot, ListPreferences(), listPositionResets.ToArray());
                listPositionResets.Clear();
            }
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }
    private ParsingListPreferences ListPreferences() => new()
    {
        Tab = activeTab, ZoneQuery = zoneQuery, ZoneState = zoneState, Query = query,
        Group = groupFilter, Source = sourceFilter, Status = statusFilter, GroupVisibility = groupVisibility,
        HistoryQuery = historyQuery, HistorySource = historySource, HistoryResult = historyFilter,
        HistoryCount = historyRequestedCount
    };
    private void ApplyListPreferences(ParsingListPreferences p)
    {
        static string Text(string? s) => s is null ? "" : s[..Math.Min(s.Length, 200)];
        static string Choice(string? value, params string[] allowed) => value is not null && allowed.Contains(value) ? value : allowed[0];
        activeTab = view!.CanReadCollection ? Choice(p.Tab, "home", "zones", "searches", "history") : "zones";
        zoneQuery = Text(p.ZoneQuery); zoneState = Choice(p.ZoneState, "active", "all", "archived");
        query = Text(p.Query); sourceFilter = Choice(p.Source, "all", "Avito", "Cian");
        statusFilter = Choice(p.Status, "all", "active", "paused", "attention");
        groupVisibility = Choice(p.GroupVisibility, "active", "all", "archived");
        groupFilter = p.Group is "all" or "none" || view.Groups.Any(g => g.Id.ToString() == p.Group) ? p.Group! : "all";
        historyQuery = Text(p.HistoryQuery); historySource = Choice(p.HistorySource, "all", "Avito", "Cian");
        historyFilter = Choice(p.HistoryResult, "all", "completed", "attention", "running");
        historyRequestedCount = Math.Clamp(p.HistoryCount / 50 * 50, 50, 10000);
    }
    private async Task DisposeListsAsync()
    {
        if (listsModule is null) return;
        try { await listsModule.InvokeVoidAsync("dispose", listsRoot); await listsModule.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }
}

public sealed class ParsingListPreferences
{
    public string? Tab { get; set; }
    public string? ZoneQuery { get; set; }
    public string? ZoneState { get; set; }
    public string? Query { get; set; }
    public string? Group { get; set; }
    public string? Source { get; set; }
    public string? Status { get; set; }
    public string? GroupVisibility { get; set; }
    public string? HistoryQuery { get; set; }
    public string? HistorySource { get; set; }
    public string? HistoryResult { get; set; }
    public int HistoryCount { get; set; } = 50;
}
