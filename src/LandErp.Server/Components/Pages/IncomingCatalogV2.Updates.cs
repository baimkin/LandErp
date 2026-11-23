using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using Microsoft.JSInterop;
using System.Text.Json;

namespace LandErp.Server.Components.Pages;

public partial class IncomingCatalogV2
{
    private int detailRequest;
    private Guid? detailRequestedId, detailLoadingId;
    private string? detailLoadError, detailReviewWarning, transferPreparationError;
    private CancellationTokenSource? detailCancellation;
    private bool transferPreparing;

    public void Dispose()
    {
        incomingDisposed = true;
        CancelSearchDebounce();
        pageRequest++;
        pageCancellation?.Cancel(); pageCancellation?.Dispose(); pageCancellation = null;
        CancelInformation();
        detailRequest++;
        detailCancellation?.Cancel(); detailCancellation?.Dispose(); detailCancellation = null;
        GC.SuppressFinalize(this);
    }

    private async Task RetryDetailReadAsync()
    {
        if (Busy || detailLoadingId != null || detailRequestedId is not Guid id) return;
        int request = detailRequest;
        detailCancellation?.Dispose(); detailCancellation = new();
        CancellationToken token = detailCancellation.Token;
        detailLoadError = null; detailLoadingId = id; ClearFeedback();
        try
        {
            // Retry only the card read. Never resend a review or a transfer because its read failed.
            if (request == detailRequest) await LoadDetailAsync(id, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (request == detailRequest) detailLoadError = ReadFailure(exception, "Не удалось открыть объявление. Повторите загрузку карточки."); }
        finally { if (request == detailRequest) detailLoadingId = null; }
    }

    private async Task RefreshReviewedItemAsync(Guid id)
    {
        InvalidateInformation();
        var before = catalogPage;
        var applied = renderedCalculationFilter;
        if (before == null || applied == null) return;
        var changed = await IncomingReads.ReadItemsAsync(CurrentSubject, applied, new[] { id }, CancellationToken.None);
        if (catalogPage != before || renderedCalculationFilter != applied) return;
        if (!before.Items.Any(item => item.Id == id)) return;
        await PreserveIncomingPositionAsync(async () =>
        {
            if (changed.Items.Count == 0)
            {
                RemoveLocalRow(id);
                await FillChangedPageAsync();
            }
            else
            {
                var rows = before.Rows.ToDictionary();
                rows[id] = changed.Rows[id];
                catalogPage = before with
                {
                    Items = before.Items.Select(item => item.Id == id ? changed.Items[0] : item).ToArray(), Rows = rows
                };
            }
            StateHasChanged();
        });
    }

    private void MarkTransferredLocally(IReadOnlyList<Guid> ids, TakeToWorkResult result)
    {
        InvalidateInformation();
        CatalogItemView Updated(CatalogItemView item) => item with
        {
            Disposition = CatalogDisposition.InWork, PropertyCaseId = result.CaseId, BusinessNumber = result.BusinessNumber
        };
        if (catalogPage != null) catalogPage = catalogPage with
        { Items = catalogPage.Items.Select(item => ids.Contains(item.Id) ? Updated(item) : item).ToArray() };
        if (detail != null && ids.Contains(detail.Item.Id))
        {
            // Even if the following read fails, the acknowledged transfer must not be offered again.
            detail = detail with { Item = Updated(detail.Item) };
            if (detailRead != null) detailRead = detailRead with { Detail = detail };
        }
    }

    private async Task ApplyTransferredScopeAsync(IncomingCatalogChangeScope before, IncomingCatalogChangeScope after)
    {
        if (catalogPage == null || renderedCalculationFilter == null) return;
        var affected = before.Ids.Concat(after.Ids).ToHashSet();
        foreach (Guid id in affected) calculationSelected.Remove(id);
        calculationExcluded.ExceptWith(before.MatchingIds.Except(after.MatchingIds));
        selectedCohortStamp = null;
        int total = Math.Max(0, catalogPage.Total + after.MatchingIds.Count - before.MatchingIds.Count);
        var retained = catalogPage.Items.Where(item => !affected.Contains(item.Id) || after.MatchingIds.Contains(item.Id)).ToArray();
        var retainedIds = retained.Select(item => item.Id).ToHashSet();
        catalogPage = catalogPage with
        {
            Total = total, Items = retained, Rows = catalogPage.Rows.Where(row => retainedIds.Contains(row.Key)).ToDictionary()
        };
        if (allCalculationResults) calculationTotal = total;
        if (after.MatchingIds.Count == 0) await FillChangedPageAsync();
        else
        {
            // All-state/processed/participant views may retain or gain sources. Read only this
            // bounded window to keep ordering correct, without global facets or markets.
            page = Math.Min(page, TotalPages - 1);
            renderedCalculationFilter = renderedCalculationFilter with
            { Base = renderedCalculationFilter.Base with { Offset = page * pageSize, Size = pageSize } };
            var window = await IncomingReads.ReadWindowAsync(CurrentSubject, renderedCalculationFilter, CancellationToken.None);
            catalogPage = catalogPage with { Items = window.Items, Rows = window.Rows };
        }
    }

    private async Task PreserveIncomingPositionAsync(Func<Task> update)
    {
        IJSObjectReference? module = null;
        JsonElement? position = null;
        try
        {
            try
            {
                module = await IncomingJs.InvokeAsync<IJSObjectReference>("import", "/js/incoming-list-position.js");
                if (module != null) position = await module.InvokeAsync<JsonElement>("capture", incomingRoot);
            }
            catch (Exception exception) when (exception is JSException or InvalidOperationException) { }
            await update();
        }
        finally
        {
            if (module != null)
            {
                try
                {
                    if (position != null) await module.InvokeVoidAsync("restore", incomingRoot, position);
                    await module.DisposeAsync();
                }
                catch (Exception exception) when (exception is JSException or InvalidOperationException) { }
            }
        }
    }
}
