using System.Text.Json;
using System.Text.Json.Serialization;

namespace LandErp.Collector.Contracts.V1;

public enum ListingSource { Avito, Cian }
public enum ListingLandType { Izhs, Snt, Dnp, Lph, Gardening, Kfh, Industrial, Other }
public enum ListingContactType { Phone, Email, Telegram, WhatsApp, Website, Other }
public enum FieldPresence { NotInspected, Absent, Empty, Present, ParseFailed }
public enum CollectionOutcome { Unknown, Success, Captcha, AuthenticationRequired, RateLimited, SourceError, Interrupted, LimitReached, Partial }
public enum AgentRuntimeState { Idle, Claiming, Parsing, AwaitingManualAction, Delivering, Paused, Recovering }
public enum SourceRuntimeState { Ready, Captcha, AuthenticationRequired, RateLimited, SourceError, Unknown }
public enum CollectionProgressPhase { Preparing, OpeningPage, ReadingPage, SavingPage, WaitingForUser, PreparingResult, DeliveringResult }
public sealed record TextField(FieldPresence Presence, string? Raw);
public sealed record DecimalField(FieldPresence Presence, string? Raw, decimal? Parsed);
public sealed record ListingContactData
{
    public required ListingContactType Type { get; init; }
    public required string Value { get; init; }
    public string? DisplayValue { get; init; }
    public bool IsPrimary { get; init; }
}
public sealed record AgentRegistration(int ContractVersion, string Version, ListingSource[] Capabilities);
public sealed record CollectionProgress(int? Page, int? MaxPages, int ProcessedCount, int? TotalCount,
    CollectionProgressPhase Phase, DateTimeOffset? LastUsefulActionAt);
public sealed record AgentHeartbeat(Guid? JobId = null, Guid? LeaseId = null, CollectionOutcome? SourceStatus = null,
    AgentRuntimeState? RuntimeState = null, SourceRuntimeState? SourceState = null, CollectionProgress? Progress = null);
public sealed record AgentActivation(Guid AgentId, string ActivationSecret, string MachineName,
    int ContractVersion, string Version, ListingSource[] Capabilities);
public sealed record AgentActivationReceipt(Guid AgentId, string Credential, int ContractVersion, string AgentName);
public sealed record CollectionWork(Guid JobId, Guid LeaseId, DateTimeOffset LeaseExpiresAt,
    ListingSource Source, string SearchUrl, int MaxPages, string Label);
public sealed record ObservationEnvelope(string ObservationKey, ListingData Data);
public sealed record CollectionCoverage(int UniqueObserved, int? SourceCountHint, bool EndReached,
    bool LoadingCompleted, int StableRounds, int CompletedPages, int RequestedPageLimit, int? ResponseBatches = null);
public sealed record CollectionResult(Guid ResultId, Guid JobId, Guid LeaseId, CollectionOutcome Outcome,
    ObservationEnvelope[] Observations, bool Final, string ReasonCode = "", string[]? Warnings = null,
    CollectionCoverage? Coverage = null);
public sealed record CollectionReceipt(Guid ResultId, string Status, int Accepted, int Duplicates,
    int NewListings = 0, int ChangedListings = 0);

public static class CollectionResultReasonCodes
{
    public const string CountHintMismatch = "COUNT_HINT_MISMATCH";
    public const string EndNotConfirmed = "END_NOT_CONFIRMED";
    public const string LoadingInterrupted = "LOADING_INTERRUPTED";
    public const string NetworkTimeout = "NETWORK_TIMEOUT";
    public const string SourceUnavailable = "SOURCE_UNAVAILABLE";
    public const string LayoutChanged = "LAYOUT_CHANGED";
    public const string InvalidSearchUrl = "INVALID_SEARCH_URL";
    public const string InvalidSourceResponse = "INVALID_SOURCE_RESPONSE";
    public const string PageLimitReached = "PAGE_LIMIT_REACHED";
    public const string AgentInterrupted = "AGENT_INTERRUPTED";
    public const string LeaseExpiredOrReplaced = "LEASE_EXPIRED_OR_REPLACED";
}

/// <summary>Public listing fields only. Browser state, cookies and raw HTTP/HTML are outside the contract.</summary>
public sealed record ListingData
{
    public required ListingSource Source { get; init; }
    public required string ExternalId { get; init; }
    public required string Url { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public required string AdapterVersion { get; init; }
    public required string Provenance { get; init; }
    public TextField Title { get; init; } = new(FieldPresence.NotInspected, null);
    public TextField Location { get; init; } = new(FieldPresence.NotInspected, null);
    public TextField Description { get; init; } = new(FieldPresence.NotInspected, null);
    public TextField SellerName { get; init; } = new(FieldPresence.NotInspected, null);
    public TextField CadastralNumber { get; init; } = new(FieldPresence.NotInspected, null);
    public DateTimeOffset? SourcePublishedAt { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public ListingLandType[] DeclaredLandTypes { get; init; } = [];
    public ListingContactData[] Contacts { get; init; } = [];
    public DecimalField Price { get; init; } = new(FieldPresence.NotInspected, null, null);
    public DecimalField AreaSquareMeters { get; init; } = new(FieldPresence.NotInspected, null, null);
    public string Currency { get; init; } = "RUB";
    public string[] PhotoUrls { get; init; } = [];
    public string[] Warnings { get; init; } = [];
}

public static class CollectionJson
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
