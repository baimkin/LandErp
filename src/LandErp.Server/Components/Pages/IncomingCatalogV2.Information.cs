using LandErp.Application.Modules.Catalog.Contracts;

namespace LandErp.Server.Components.Pages;

public partial class IncomingCatalogV2
{
    private IncomingCatalogInformation? information;
    private CancellationTokenSource? pageCancellation, informationCancellation;
    private int pageRequest, informationMutation, informationRequest;
    private string? loadingInformationKey;
    private bool informationPending, informationLoading, incomingDisposed;
    private string? informationError, marketError;
    private string CountLabel(int? count) => information == null ? "—" : (count ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture);

    private CancellationToken BeginPageRead()
    {
        pageRequest++;
        pageCancellation?.Cancel(); pageCancellation?.Dispose(); pageCancellation = new();
        return pageCancellation.Token;
    }

    private void CancelInformation()
    {
        informationRequest++;
        informationCancellation?.Cancel(); informationCancellation?.Dispose(); informationCancellation = null;
        informationPending = informationLoading = false;
    }

    // Called only after acknowledged writes. Never replace a local Total with a secondary response.
    private void InvalidateInformation()
    {
        informationMutation++;
        CancelInformation();
        if (information != null) information = information with { FreshAt = DateTimeOffset.MinValue };
    }

    private void PrepareInformation(bool presentationOnly)
    {
        if (catalogPage == null) return;
        if (informationLoading)
        {
            // Pagination may reuse the same in-flight calculation, including on its first
            // cache miss. A new scope or an explicit refresh cancels the old calculation.
            if (presentationOnly && loadingInformationKey == catalogPage.InformationScopeKey)
            {
                if (information?.ScopeKey == catalogPage.InformationScopeKey)
                    catalogPage = catalogPage with { Summary = information.Summary, FilterCounts = information.FilterCounts };
                return;
            }
            CancelInformation();
        }
        if (information?.ScopeKey != catalogPage.InformationScopeKey)
        {
            information = null; groupMarket = null;
        }
        if (information != null)
            catalogPage = catalogPage with { Summary = information.Summary, FilterCounts = information.FilterCounts };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool sameBusinessDay = information != null && information.FreshAt != DateTimeOffset.MinValue
            && information.FreshAt.ToOffset(TimeSpan.FromHours(3)).Date == now.ToOffset(TimeSpan.FromHours(3)).Date;
        if (presentationOnly && sameBusinessDay && now - information!.FreshAt < TimeSpan.FromSeconds(60)) return;
        informationError = marketError = null;
        informationPending = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (informationPending && !Loading && !Busy && !incomingDisposed)
            await LoadInformationAsync();
    }

    private async Task LoadInformationAsync()
    {
        if (!informationPending || informationLoading || catalogPage == null || renderedCalculationFilter == null) return;
        informationPending = false; informationLoading = true;
        int request = informationRequest, mutation = informationMutation;
        string key = catalogPage.InformationScopeKey;
        loadingInformationKey = key;
        var applied = renderedCalculationFilter;
        informationCancellation?.Dispose(); informationCancellation = new();
        CancellationToken token = informationCancellation.Token;
        bool Current() => !incomingDisposed && !token.IsCancellationRequested && request == informationRequest
            && mutation == informationMutation && !Busy && catalogPage?.InformationScopeKey == key;
        try
        {
            try
            {
                var result = await IncomingReads.ReadInformationAsync(CurrentSubject, applied, token);
                if (Current())
                {
                    if (result.ScopeKey == key)
                    {
                        information = result;
                        catalogPage = catalogPage! with { Summary = result.Summary, FilterCounts = result.FilterCounts };
                    }
                    else informationError = "Условия отбора изменились. Обновите список для обновления счётчиков.";
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (Current()) informationError = ReadFailure(exception, "Не удалось обновить счётчики. Список доступен.");
            }
            if (!Current()) return;
            // Secondary reads are sequential and have their own errors, never a Task per preset.
            try
            {
                var market = await Markets.ReadIncomingAsync(CurrentSubject, applied, token);
                if (Current()) groupMarket = market;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (Current()) marketError = ReadFailure(exception, "Не удалось обновить рынок. Список доступен.");
            }
        }
        finally
        {
            if (request == informationRequest && mutation == informationMutation && !incomingDisposed)
            {
                informationLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }
}
