using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Overview.Contracts;

public sealed record GroupMarketView(Guid SearchGroupId, string Name, decimal? MedianPricePerSotka,
    decimal? AveragePricePerSotka, int ParticipantCount, decimal? DemandTestPricePerSotka,
    long Version, bool CanEditDemand, bool CanReadParticipants)
{
    public decimal? TargetPurchasePricePerSotka { get; init; }
}
public sealed record SaveDemandTestPrice(Guid SearchGroupId, long ExpectedVersion, decimal? PricePerSotka);
public sealed record SaveTargetPurchasePrice(Guid SearchGroupId, long ExpectedVersion, decimal? PricePerSotka);

public interface IGroupMarketService
{
    Task<GroupMarketView?> ReadIncomingAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken cancellationToken);
    Task<IReadOnlyList<IncomingSearchGroupView>> ReadProcurementGroupsAsync(Subject subject, CancellationToken cancellationToken);
    Task<GroupMarketView?> ReadProcurementAsync(Subject subject, Guid? groupId, CancellationToken cancellationToken);
    Task<IReadOnlyList<GroupMarketView>> ReadCaseAsync(Subject subject, Guid caseId, CancellationToken cancellationToken);
    Task SaveDemandAsync(Subject subject, SaveDemandTestPrice command, string correlationId, CancellationToken cancellationToken);
    Task SaveTargetPurchaseAsync(Subject subject, SaveTargetPurchasePrice command, string correlationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Редактирование нужной цены покупки не поддержано реализацией.");
}
