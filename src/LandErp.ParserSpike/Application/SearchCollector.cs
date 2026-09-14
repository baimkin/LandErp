using LandErp.ParserSpike.Avito;
using LandErp.ParserSpike.Contracts;
using LandErp.ParserSpike.Serialization;
using LandErp.ParserSpike.Storage;

namespace LandErp.ParserSpike.Application;

public interface ISearchPage
{
    string CurrentUrl { get; }
    Task<SearchParseResult> ReadAsync();
    Task<bool> ScrollAsync(CancellationToken cancellationToken);
    Task<string?> NextUrlAsync();
    Task GoNextAsync(string expectedUrl, CancellationToken cancellationToken);
}

public sealed record CollectionProgress(int Page, int Limit, int Count, string State);

/// <summary>A bounded, manually resumable run; never retries protection pages automatically.</summary>
public sealed class SearchCollector
{
    private readonly ISearchPage source;
    private readonly ListingStore store;
    private readonly Dictionary<string, SearchListing> items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> fingerprints = new(StringComparer.Ordinal);
    private readonly HashSet<string> visited = new(StringComparer.Ordinal);
    private readonly HashSet<string> qualityErrors = new(StringComparer.Ordinal);
    private int steps;
    private int quietBottom;
    private bool lastBottom;
    private bool pageComplete;

    public SearchCollector(ISearchPage source, ListingStore store, int pageLimit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(store);
        if (pageLimit is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(pageLimit));
        this.source = source;
        this.store = store;
        Limit = pageLimit;
        RunId = store.StartRun(source.CurrentUrl, pageLimit);
        visited.Add(source.CurrentUrl);
    }

    public string RunId { get; }
    public int PageNumber { get; private set; } = 1;
    public int Limit { get; }
    public bool CanResume { get; private set; }
    public SearchParseResult? LastResult { get; private set; }
    public SearchListing[] Listings => items.Values.ToArray();
    public string State { get; private set; } = "READY";
    public string[] QualityErrors => qualityErrors.ToArray();

    public void Stop()
    {
        CanResume = false;
        State = "STOPPED";
        store.UpdateRun(RunId, State, PageNumber, items.Count);
    }

    public async Task CollectAsync(IProgress<CollectionProgress>? progress, CancellationToken cancellationToken)
    {
        CanResume = false;
        SetState("RUNNING");
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastResult = await source.ReadAsync().ConfigureAwait(false);
                PageClassification classification = LastResult.Metadata.Classification;
                if (classification != PageClassification.SearchResults || LastResult.Listings.Length == 0)
                {
                    CanResume = classification is PageClassification.Captcha or PageClassification.AuthenticationRequired or
                        PageClassification.RateLimited or PageClassification.Unknown or PageClassification.SourceError;
                    SetState(classification == PageClassification.SearchResults ? "NO_VALID_ITEMS" : classification.ToString());
                    return;
                }
                foreach (string error in LastResult.Metadata.Errors) qualityErrors.Add(error);
                SearchListing[] changed = LastResult.Listings.Where(item =>
                {
                    string key = PageNumber + ":" + item.ExternalId;
                    string json = SpikeJson.Serialize(item);
                    if (fingerprints.TryGetValue(key, out string? prior) && prior == json) return false;
                    fingerprints[key] = json;
                    return true;
                }).ToArray();
                // Save first, then publish progress. Cancellation never discards saved observations.
                if (changed.Length > 0)
                {
                    store.Save(RunId, PageNumber, LastResult.Metadata.ObservedAtUtc, changed);
                    foreach (SearchListing item in changed) items[item.ExternalId] = item;
                    store.UpdateRun(RunId, State, PageNumber, items.Count);
                }
                progress?.Report(new(PageNumber, Limit, items.Count, State));
                if (lastBottom && changed.Length == 0) quietBottom++; else quietBottom = 0;
                if (pageComplete || quietBottom >= 3)
                {
                    pageComplete = true;
                    if (PageNumber >= Limit) { SetState(qualityErrors.Count == 0 ? "COMPLETED" : "COMPLETED_WITH_WARNINGS"); return; }
                    string? next = await source.NextUrlAsync().ConfigureAwait(false);
                    if (next is null) { SetState("END_OF_RESULTS"); return; }
                    if (!Uri.TryCreate(next, UriKind.Absolute, out Uri? nextUri) || !SearchParser.IsAvitoUrl(nextUri)
                        || visited.Contains(next)) { SetState("INVALID_NEXT_PAGE"); return; }
                    cancellationToken.ThrowIfCancellationRequested();
                    // Advance before clicking: a timeout/cancellation may occur after navigation.
                    PageNumber++;
                    steps = quietBottom = 0;
                    lastBottom = pageComplete = false;
                    visited.Add(next);
                    await source.GoNextAsync(next, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (steps >= 120) { SetState("SCROLL_LIMIT_INCOMPLETE"); return; }
                steps++;
                lastBottom = await source.ScrollAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { SetState("STOPPED"); }
        catch
        {
            SetState("ERROR");
            throw;
        }
        finally { progress?.Report(new(PageNumber, Limit, items.Count, State)); }

        void SetState(string state)
        {
            State = state;
            store.UpdateRun(RunId, state, PageNumber, items.Count);
        }
    }
}
