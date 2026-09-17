using System.Text;
using System.Text.Json;

namespace LandErp.Collector.Contracts.V1;

public enum CollectorScheduleKind { Manual, Interval, FixedTimes }
public sealed record RunCollectorSearch(Guid SearchId);

public sealed record CollectorScheduleDefinition(
    CollectorScheduleKind Kind,
    int? IntervalMinutes = null,
    string[]? FixedTimes = null);

/// <summary>Shown once by Server. The whole value is a secret because it contains the permanent machine token.</summary>
public sealed record CollectorConnectionEnvelope(int Version, string ServerOrigin, Guid AgentId, string Token);

public static class CollectorConnectionCode
{
    public static string Create(Uri origin, Guid agentId, string token)
    {
        Validate(origin, agentId, token);
        string json = JsonSerializer.Serialize(new CollectorConnectionEnvelope(1, origin.AbsoluteUri, agentId, token), CollectionJson.Options);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    public static CollectorConnectionEnvelope Parse(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 4096 || code.Any(char.IsWhiteSpace))
            throw new ArgumentException("CONNECTION_CODE_INVALID");
        try
        {
            string base64 = code.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            CollectorConnectionEnvelope value = JsonSerializer.Deserialize<CollectorConnectionEnvelope>(Convert.FromBase64String(base64), CollectionJson.Options)
                ?? throw new ArgumentException("CONNECTION_CODE_INVALID");
            Uri origin = new(value.ServerOrigin, UriKind.Absolute);
            if (value.Version != 1) throw new ArgumentException("CONNECTION_CODE_VERSION_UNSUPPORTED");
            Validate(origin, value.AgentId, value.Token);
            return value;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or UriFormatException)
        { throw new ArgumentException("CONNECTION_CODE_INVALID"); }
    }
    private static void Validate(Uri origin, Guid agentId, string token)
    {
        if (origin.Scheme != Uri.UriSchemeHttps || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/"
            || origin.Query.Length != 0 || origin.Fragment.Length != 0 || agentId == Guid.Empty
            || token.Length != 64 || !token.All(char.IsAsciiHexDigit)) throw new ArgumentException("CONNECTION_CODE_INVALID");
    }
}

public sealed record CollectorGroupView(
    Guid Id,
    string Name,
    int SortOrder,
    bool Active,
    long Revision);

public sealed record CollectorSearchView(
    Guid Id,
    string Label,
    ListingSource Source,
    string Url,
    int MaxPages,
    Guid? GroupId,
    CollectorScheduleDefinition Schedule,
    bool Enabled,
    long Revision);

public sealed record CollectorWorkspace(
    CollectorGroupView[] Groups,
    CollectorSearchView[] Searches,
    string[] GrantedFeatures);

public sealed record CreateCollectorGroup(
    Guid CommandId,
    string Name,
    int SortOrder);

public sealed record UpdateCollectorGroup(
    Guid Id,
    long ExpectedRevision,
    string Name,
    int SortOrder,
    bool Active);

public sealed record CreateCollectorSearch(
    Guid CommandId,
    string Label,
    ListingSource Source,
    string Url,
    int MaxPages,
    Guid? GroupId,
    CollectorScheduleDefinition Schedule);

public sealed record UpdateCollectorSearch(
    Guid Id,
    long ExpectedRevision,
    string Label,
    ListingSource Source,
    string Url,
    int MaxPages,
    Guid? GroupId,
    CollectorScheduleDefinition Schedule,
    bool Enabled);

public static class CollectorFeatures
{
    public const string SearchRead = "searches.read";
    public const string SearchManage = "searches.manage";
}

public static class CollectorErrorCodes
{
    public const string AgentUnauthorized = "AGENT_UNAUTHORIZED";
    public const string VersionOrCapabilityUnsupported = "VERSION_OR_CAPABILITY_UNSUPPORTED";
    public const string RegistrationRequired = "REGISTRATION_REQUIRED";
    public const string WorkNotAllowed = "WORK_NOT_ALLOWED";
    public const string AgentBusy = "AGENT_BUSY";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string ObservationKeyConflict = "OBSERVATION_KEY_CONFLICT";
    public const string LeaseExpiredOrReplaced = "LEASE_EXPIRED_OR_REPLACED";
    public const string SourceUnsupported = "SOURCE_UNSUPPORTED";
    public const string SearchPermissionRequired = "SEARCH_PERMISSION_REQUIRED";
}
