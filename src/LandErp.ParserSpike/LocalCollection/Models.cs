using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LandErp.ParserSpike.Contracts;

namespace LandErp.ParserSpike.LocalCollection;

public enum SourceSite { Avito, Cian }
public enum JobState { Pending, Running, PausedByUser, AwaitingManualAction, Failed, StoppedInterrupted, Completed, LimitReached, SkippedFresh }
public enum PageKind { SearchResults, Captcha, AuthenticationRequired, RateLimited, SourceError, Unknown }
public enum NextKind { Next, End, UnknownInvalid }
public enum ErrorPolicy { Continue, PauseSource }

/// <summary>Text observations retain absence separately from legacy fields whose presence was never recorded.</summary>
public sealed record TextValue(Presence Presence, string? Raw)
{
    public static TextValue Read(string? text) => text is null ? new(Presence.Absent, null)
        : new(text.Length == 0 ? Presence.Empty : Presence.Present, text);
    public static TextValue Legacy(string? text) => new(Presence.NotInspected, text);
}

public sealed record NumberValue(Presence Presence, string? Raw, decimal? Parsed)
{
    public static NumberValue Read(string? raw)
    {
        if (raw is null) return new(Presence.Absent, null, null);
        if (raw.Length == 0) return new(Presence.Empty, raw, null);
        string digits = System.Text.RegularExpressions.Regex.Replace(raw.Replace(',', '.'), @"[^\d.\-]", "");
        return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? new(Presence.Present, raw, value) : new(Presence.ParseFailed, raw, null);
    }
}

public sealed record AreaAssertion(string Origin, string Raw, decimal SquareMeters);

/// <summary>Version 1 local envelope; the accepted Gate 01 JSON contract remains a separate format.</summary>
public sealed record ListingObservation
{
    public int SchemaVersion { get; init; } = 1;
    public string AdapterVersion { get; init; } = "1.0";
    public required SourceSite Source { get; init; }
    public required string ExternalId { get; init; }
    public required string Url { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public TextValue Title { get; init; } = TextValue.Read(null);
    public NumberValue Price { get; init; } = NumberValue.Read(null);
    public NumberValue UnitPrice { get; init; } = NumberValue.Read(null);
    public decimal? DerivedPricePerSotka { get; init; }
    public NumberValue AreaSquareMeters { get; init; } = NumberValue.Read(null);
    public AreaAssertion[] Areas { get; init; } = [];
    public TextValue Assignment { get; init; } = TextValue.Read(null);
    public TextValue Location { get; init; } = TextValue.Read(null);
    public TextValue Transport { get; init; } = TextValue.Read(null);
    public TextValue Description { get; init; } = TextValue.Read(null);
    public TextValue DateText { get; init; } = TextValue.Read(null);
    public TextValue SellerName { get; init; } = TextValue.Read(null);
    public TextValue SellerUrl { get; init; } = TextValue.Read(null);
    public TextValue SellerType { get; init; } = TextValue.Read(null);
    public TextValue SellerStatistics { get; init; } = TextValue.Read(null);
    public NumberValue CompletedAdvertisements { get; init; } = NumberValue.Read(null);
    public NumberValue Latitude { get; init; } = NumberValue.Read(null);
    public NumberValue Longitude { get; init; } = NumberValue.Read(null);
    /// <summary>Source code of precision; its units are not established.</summary>
    public NumberValue CoordinatePrecision { get; init; } = NumberValue.Read(null);
    public string[] Badges { get; init; } = [];
    public string[] PhotoUrls { get; init; } = [];
    public string[] Warnings { get; init; } = [];
    public string Provenance { get; init; } = "DOM";
    public string DisplayPrice => Price.Raw ?? "Нет данных";
}

public sealed record GeoPoint(decimal Longitude, decimal Latitude);
/// <summary>Confirmed source polygon, not the viewport rectangle encoded in the URL.</summary>
public sealed record MapScope(string? DrawId, bool RequiresDraw, bool ZoneConfirmed, GeoPoint[][] Rings,
    int? ExpectedCount, int ResponseBatches, string? ZoneError = null);

/// <summary>Only public numeric IDs and counters, never raw response values.</summary>
public sealed record MapBatchDiagnostic(int Batch, int Received, int NewIds, int Repeated, int Recommendations,
    int InvalidIdOrUrl, int InvalidCards, int MissingCoordinates, int OutsidePolygon, string[] Ids);
public sealed record MapDiagnostic(MapBatchDiagnostic[] Batches, int? ExpectedCount, int JsonUnique, int Collected,
    string[] MissingCoordinates, string[] OutsidePolygon, string[] DomSeenIds, string[] DomWithoutJson,
    string[] DomNotCollected, bool ZoneConfirmed, bool Loading, double? ScrollTop = null,
    double? ClientHeight = null, double? ScrollHeight = null, string? ScrollError = null);

public sealed record PageObservation(PageKind Kind, ListingObservation[] Listings, string[] Warnings, bool Loading = false, string? Layout = null, MapScope? Map = null, ListingObservation[]? Changes = null, MapDiagnostic? Diagnostic = null);
public sealed record Pagination(NextKind Kind, string? Url = null, string? Selector = null, string? Reason = null);
public sealed record SearchLink(string Id, string Label, string Url, SourceSite Source, bool Selected, bool Enabled, int Revision, bool Archived = false);
public sealed record CollectionJob(string Id, string BatchId, string LinkId, int Revision, SourceSite Source,
    string Url, string PassId, int Page, int Limit, JobState State, string Reason, string? Owner, string? Token,
    DateTimeOffset StartedAtUtc)
{
    public string DisplayState => State switch
    {
        JobState.Pending => "В очереди",
        JobState.Running => "Обрабатывается",
        JobState.PausedByUser => "Пауза",
        JobState.AwaitingManualAction => "Нужна проверка",
        JobState.Failed => "Ошибка",
        JobState.StoppedInterrupted => "Прервано",
        JobState.Completed => "Вся выдача",
        JobState.LimitReached => "Предел страниц",
        JobState.SkippedFresh => "Свежие данные",
        _ => State.ToString()
    };
}
public sealed record PageJournal(string JobId, int Page, string Url, DateTimeOffset Time, int Count, string Reason)
{
    public int NewCount { get; init; }
    public int RepeatCount { get; init; }
    public bool Completed { get; init; }
}
public sealed record ListingRow(ListingObservation Observation, DateTimeOffset FirstSeen, DateTimeOffset LastSeen)
{
    public SourceSite Source => Observation.Source;
    public string ExternalId => Observation.ExternalId;
    public string Title => Observation.Title.Raw ?? "";
    public string Price => Observation.DisplayPrice;
    public string Location => Observation.Location.Raw ?? "";
    public string Seller => Observation.SellerName.Raw ?? "";
    public decimal? Latitude => Observation.Latitude.Parsed;
    public decimal? Longitude => Observation.Longitude.Parsed;
}
public sealed record ListingFilter(string Text = "", SourceSite? Source = null, string? LinkId = null,
    string? JobId = null, int Offset = 0, int Size = 100, bool PriceOrder = false);
public sealed record ListingPage(ListingRow[] Rows, long Total);
public sealed record HistoryRow(string Id, string JobId, int Page, ListingObservation Observation);

/// <summary>Each batch copies validated settings. Changes made during a run affect the next batch.</summary>
public sealed record CollectionSettings
{
    public int Version { get; init; } = 1;
    public int MaxPages { get; init; } = 10;
    public double FreshnessHours { get; init; } = 24;
    public double ResumeHours { get; init; } = 1;
    public int AvitoTabs { get; init; } = 1;
    public int CianTabs { get; init; } = 1;
    public int GlobalTabs { get; init; } = 6;
    public string Browser { get; init; } = "chrome";
    public int WheelDelta { get; init; } = 100;
    public int WheelIntervalMilliseconds { get; init; } = 20;
    public int ScrollBatchEvents { get; init; } = 12;
    public int ScrollPauseMilliseconds { get; init; } = 40;
    public int LoadWaitSeconds { get; init; } = 5;
    public int StabilitySeconds { get; init; } = 3;
    public int MaxScrollSteps { get; init; } = 300;
    public int NavigationTimeoutSeconds { get; init; } = 30;
    public int ElementTimeoutSeconds { get; init; } = 10;
    public int PageIntervalSeconds { get; init; } = 3;
    public int LinkIntervalSeconds { get; init; } = 5;
    public ErrorPolicy ErrorPolicy { get; init; }

    public void Validate()
    {
        static void Range(double value, double minimum, double maximum, string name)
        { if (!double.IsFinite(value) || value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name); }
        if (Version != 1) throw new ArgumentException("SETTINGS_VERSION");
        Range(MaxPages, 1, 100, nameof(MaxPages)); Range(FreshnessHours, 0, 168, nameof(FreshnessHours));
        Range(ResumeHours, 0, 24, nameof(ResumeHours)); Range(AvitoTabs, 1, 3, nameof(AvitoTabs));
        Range(CianTabs, 1, 3, nameof(CianTabs)); Range(GlobalTabs, 1, 6, nameof(GlobalTabs));
        Range(WheelDelta, 10, 120, nameof(WheelDelta)); Range(WheelIntervalMilliseconds, 20, 250, nameof(WheelIntervalMilliseconds));
        Range(ScrollBatchEvents, 1, 30, nameof(ScrollBatchEvents)); Range(ScrollPauseMilliseconds, 0, 2000, nameof(ScrollPauseMilliseconds));
        Range(LoadWaitSeconds, 1, 30, nameof(LoadWaitSeconds)); Range(StabilitySeconds, 1, 15, nameof(StabilitySeconds));
        Range(MaxScrollSteps, 20, 2000, nameof(MaxScrollSteps)); Range(NavigationTimeoutSeconds, 5, 120, nameof(NavigationTimeoutSeconds));
        Range(ElementTimeoutSeconds, 2, 60, nameof(ElementTimeoutSeconds)); Range(PageIntervalSeconds, 1, 60, nameof(PageIntervalSeconds));
        Range(LinkIntervalSeconds, 1, 120, nameof(LinkIntervalSeconds));
        if (Browser is not ("chrome" or "msedge") || !Enum.IsDefined(ErrorPolicy)) throw new ArgumentException("SETTINGS_VALUE");
    }
    // Timing, tab counts and requested limit do not change the meaning of a committed page.
    public string Compatibility => $"local:{Version}/adapters:1/main-results/all-fields";
}

public static class LocalJson
{
    private static readonly JsonSerializerOptions Options = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() }, WriteIndented = true };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("NULL_DOCUMENT");
}

/// <summary>UI, browser and SQL depend on these boundaries, rather than on another source's adapter.</summary>
public interface ISourcePage : IAsyncDisposable
{
    SourceSite Source { get; }
    string CurrentUrl { get; }
    Task OpenAsync(string url, CancellationToken cancellationToken);
    Task<PageObservation> ReadAsync(CancellationToken cancellationToken);
    Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken);
    Task<Pagination> NextAsync(CancellationToken cancellationToken);
    Task FollowAsync(Pagination pagination, CancellationToken cancellationToken);
    Task ActivateAsync(CancellationToken cancellationToken);
}
public interface ISourceSessions : IAsyncDisposable
{
    Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken);
}
public interface IJobSource
{
    CollectionJob? Claim(string batchId, SourceSite source, string owner);
    bool SetState(CollectionJob job, JobState state, string reason);
}
public interface IResultSink
{
    bool SavePage(CollectionJob job, int page, string pageUrl, IReadOnlyList<ListingObservation> listings,
        bool completed, Pagination? pagination, string reason);
}
