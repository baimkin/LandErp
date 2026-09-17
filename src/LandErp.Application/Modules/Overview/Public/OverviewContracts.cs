using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Overview.Contracts;

public enum OverviewSeverity { Info, Warning, Critical }
public enum OverviewDueState { None, Upcoming, Today, Overdue }
public enum MarketGroupSort { Name, MedianDescending, MedianAscending, SampleDescending }

public sealed record OverviewMetric(int Value, string Caption);
public sealed record OverviewAttentionItem(string Key, OverviewSeverity Severity, string Title, string Reason,
    string Age, string Action, string Url);
public sealed record OverviewStageItem(string Key, string Label, int Count, string Caption, int Percent, string Url);
public sealed record OverviewWorkItem(string Key, string ObjectTitle, string Context, string Action,
    string Detail, string Deadline, OverviewDueState DueState, string Url);
public sealed record OverviewCollectionStatus(OverviewSeverity Severity, string Label, string Value, string Url);
public sealed record OverviewCollectionSummary(int OnlineCollectors, int EnabledCollectors, int ActiveSearches,
    int ProcessedToday, IReadOnlyList<OverviewCollectionStatus> Statuses, string ManagementUrl);
public sealed record OverviewQuickAction(string Key, string Label, string Url, bool Primary);
public sealed record OverviewTeamMember(Guid EmployeeId, string Name, int ActiveCases, int OverdueCases);

public sealed record SearchGroupMarketSettingsView(int PeriodDays, IncomingLandType[] AllowedPropertyTypes,
    decimal? MinPricePerSotka, decimal? MaxPricePerSotka, long Version);
public sealed record MarketGroupRow(Guid SearchGroupId, string Name, int SortOrder,
    decimal? MedianPricePerSotka, decimal? AveragePricePerSotka, int IncludedCount,
    int ExcludedCount, int FakeExcludedCount, SearchGroupMarketSettingsView Settings, bool CanManage);
public sealed record MarketGroupPage(IReadOnlyList<MarketGroupRow> Items, int Total, int Offset, int Size);
public sealed record MarketGroupQuery(string Text = "", MarketGroupSort Sort = MarketGroupSort.Name,
    int Offset = 0, int Size = 20);
public sealed record SaveSearchGroupMarketSettings(Guid SearchGroupId, long ExpectedVersion, int PeriodDays,
    IncomingLandType[] AllowedPropertyTypes, decimal? MinPricePerSotka, decimal? MaxPricePerSotka);

public sealed record OverviewView(
    bool CanViewProcurement,
    OverviewMetric NewIncoming,
    OverviewMetric ActiveProcurement,
    OverviewMetric Attention,
    OverviewMetric WaitingDecision,
    IReadOnlyList<OverviewAttentionItem> AttentionItems,
    int AttentionTotal,
    IReadOnlyList<OverviewStageItem> ProcurementStages,
    IReadOnlyList<OverviewWorkItem> MyWork,
    int MyWorkTotal,
    OverviewCollectionSummary? Collection,
    IReadOnlyList<OverviewQuickAction> QuickActions,
    IReadOnlyList<OverviewTeamMember> Team,
    int TeamTotal,
    MarketGroupPage Market,
    DateTimeOffset GeneratedAt);

public interface IOverviewService
{
    Task<OverviewView> ReadAsync(Subject subject, CancellationToken cancellationToken);
    Task<MarketGroupPage> ReadMarketGroupsAsync(Subject subject, MarketGroupQuery query, CancellationToken cancellationToken);
    Task<SearchGroupMarketSettingsView> SaveMarketSettingsAsync(Subject subject,
        SaveSearchGroupMarketSettings command, string correlationId, CancellationToken cancellationToken);
}
