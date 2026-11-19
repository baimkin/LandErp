using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace LandErp.Server.Components.Pages;

public partial class IncomingCatalogV2
{
    private ElementReference incomingRoot;

    // Only use local removal when the rendered selection necessarily excludes Dismissed.
    // Archive, participants and all-state/processed views keep their own membership semantics.
    private bool CanDismissLocally => catalogPage != null && renderedCalculationFilter is { } applied
        && applied.WorkingScope?.Mode is not (IncomingCatalogMode.Archive or IncomingCatalogMode.Participants)
        && applied.WorkingScope?.Slice != IncomingCatalogPreset.ProcessedToday
        && (IsIncomingSlice(applied.WorkingScope?.Slice) || IsIncomingSlice(applied.Preset)
            || applied.Base.Disposition == CatalogDisposition.Incoming);

    private static bool IsIncomingSlice(IncomingCatalogPreset? slice) => slice is IncomingCatalogPreset.New
        or IncomingCatalogPreset.PriceChanged or IncomingCatalogPreset.Incomplete
        or IncomingCatalogPreset.ReturnedFromMonitoring or IncomingCatalogPreset.PossibleDuplicate;

    private async Task DismissLocallyAsync(Guid id, long version, string reason, int previousIndex, Guid? preferredNextId)
    {
        // Position tracking is optional; its failure must never prevent or obscure a business command.
        IJSObjectReference? positions = null;
        JsonElement? position = null;
        try
        {
            await ExecuteConfirmedAsync(async () =>
                {
                    try
                    {
                        positions = await IncomingJs.InvokeAsync<IJSObjectReference>("import", "/js/incoming-list-position.js");
                        if (positions != null) position = await positions.InvokeAsync<JsonElement>("capture", incomingRoot);
                    }
                    catch (Exception exception) when (exception is JSException or InvalidOperationException) { }
                    await Workspace.SetDispositionAsync(CurrentSubject,
                        new(id, version, CatalogDisposition.Dismissed, reason), Guid.CreateVersion7().ToString(), CancellationToken.None);
                },
                nameof(ClassifyAsync), async () =>
                {
                    // The command has acknowledged its commit. No general ReadAsync is needed here.
                    classificationOpen = false;
                    RemoveLocalRow(id);
                    CloseDetail();
                    StateHasChanged();
                    int previousPage = page;
                    await FillDismissedPageAsync();
                    if (openNextAfterDecision && page == previousPage)
                    {
                        Guid? next = catalogPage!.Items.Any(item => item.Id == preferredNextId) ? preferredNextId
                            : catalogPage.Items.ElementAtOrDefault(Math.Max(0, previousIndex))?.Id;
                        if (next != null) await OpenNextLocallyAsync(next.Value);
                    }
                    // The removed row / next card acknowledge success. A persistent floating toast
                    // would cover pagination and the next card's action buttons.
                    Success = null;
                    StateHasChanged();
                });
        }
        finally
        {
            if (positions != null)
            {
                try
                {
                    if (position != null) await positions.InvokeVoidAsync("restore", incomingRoot, position);
                    await positions.DisposeAsync();
                }
                catch (Exception exception) when (exception is JSException or InvalidOperationException) { }
            }
        }
    }

    private void RemoveLocalRow(Guid id)
    {
        calculationSelected.Remove(id);
        calculationExcluded.Remove(id);
        selectedCohortStamp = null; // Existing bulk preview must revalidate the changed cohort.
        if (catalogPage == null || catalogPage.Items.All(item => item.Id != id)) return;
        var rows = catalogPage.Rows.Where(item => item.Key != id).ToDictionary();
        catalogPage = catalogPage with
        {
            Items = catalogPage.Items.Where(item => item.Id != id).ToArray(), Rows = rows,
            Total = Math.Max(0, catalogPage.Total - 1)
        };
        if (allCalculationResults) calculationTotal = catalogPage.Total;
    }

    private async Task FillDismissedPageAsync()
    {
        if (catalogPage == null || renderedCalculationFilter == null) return;
        int targetPage = Math.Min(page, TotalPages - 1);
        bool changedPage = targetPage != page;
        page = targetPage;
        renderedCalculationFilter = renderedCalculationFilter with
        {
            Base = renderedCalculationFilter.Base with { Offset = page * pageSize, Size = pageSize }
        };
        int retained = changedPage ? 0 : catalogPage.Items.Count;
        int desired = Math.Min(pageSize, Math.Max(0, catalogPage.Total - page * pageSize));
        if (desired <= retained) return;
        // Refill the hole at the end, so the next ordinary page offset cannot skip a listing.
        var window = await IncomingReads.ReadWindowAsync(CurrentSubject, renderedCalculationFilter with
        {
            Base = renderedCalculationFilter.Base with { Offset = page * pageSize + retained, Size = desired - retained }
        }, CancellationToken.None);
        var items = (changedPage ? [] : catalogPage.Items).Concat(window.Items).DistinctBy(item => item.Id).ToArray();
        var rows = (changedPage ? new Dictionary<Guid, IncomingCatalogRowRead>() : catalogPage.Rows.ToDictionary());
        foreach (var row in window.Rows) rows[row.Key] = row.Value;
        catalogPage = catalogPage with { Items = items, Rows = rows };
    }

    private async Task OpenNextLocallyAsync(Guid id)
    {
        // Keep review history and server authorization, without the second full list/market refresh.
        await Workspace.RegisterViewAsync(CurrentSubject, id, Guid.CreateVersion7().ToString(), CancellationToken.None);
        if (catalogPage!.Rows.TryGetValue(id, out var row))
        {
            bool newOnly = renderedCalculationFilter?.WorkingScope?.Slice == IncomingCatalogPreset.New
                || renderedCalculationFilter?.Preset == IncomingCatalogPreset.New;
            if (newOnly && !row.Reviewed)
            {
                RemoveLocalRow(id);
                await FillDismissedPageAsync();
            }
            else
            {
                var rows = catalogPage.Rows.ToDictionary();
                rows[id] = row with { Reviewed = true };
                catalogPage = catalogPage with { Rows = rows };
            }
        }
        await LoadDetailAsync(id);
    }
}
