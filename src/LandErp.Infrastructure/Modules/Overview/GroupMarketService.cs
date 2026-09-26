using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Overview;

public sealed class GroupMarketService(IDbContextFactory<LandErpDbContext> factory,
    IEmployeeAccessService access, IIncomingFilterPresetService presets) : IGroupMarketService
{
    public async Task<GroupMarketView?> ReadIncomingAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken cancellationToken)
    {
        var effective = await access.ResolveAsync(subject, cancellationToken);
        if (!effective.CanReadIncoming) throw new AccessDeniedException();
        Guid? groupId = filter.SearchGroupId;
        if (filter.WorkingScope is { Mode: IncomingCatalogMode.SavedFilters, SelectedPresetId: Guid presetId, UseDraft: false })
        {
            var saved = (await presets.ReadAsync(subject, cancellationToken)).SingleOrDefault(item => item.Id == presetId)
                ?? throw new AccessDeniedException();
            groupId = saved.Criteria.SearchGroupId;
        }
        return await ReadOneAsync(effective, groupId, cancellationToken);
    }

    public async Task<IReadOnlyList<IncomingSearchGroupView>> ReadProcurementGroupsAsync(Subject subject, CancellationToken cancellationToken)
    {
        var effective = await access.ResolveAsync(subject, cancellationToken);
        if (!effective.CanReadProcurement) throw new AccessDeniedException();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.SearchGroups.AsNoTracking().Where(item => item.OrganizationId == effective.OrganizationId && item.Active)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name).ThenBy(item => item.Id)
            .Select(item => new IncomingSearchGroupView(item.Id, item.Name, item.SortOrder)).ToArrayAsync(cancellationToken);
    }

    public async Task<GroupMarketView?> ReadProcurementAsync(Subject subject, Guid? groupId, CancellationToken cancellationToken)
    {
        var effective = await access.ResolveAsync(subject, cancellationToken);
        if (!effective.CanReadProcurement) throw new AccessDeniedException();
        return await ReadOneAsync(effective, groupId, cancellationToken);
    }

    private async Task<GroupMarketView?> ReadOneAsync(EffectiveEmployeeAccess effective, Guid? groupId, CancellationToken cancellationToken)
    {
        if (groupId == null) return null;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return (await ReadCoreAsync(db, effective, [groupId.Value], cancellationToken)).SingleOrDefault() ?? throw new AccessDeniedException();
    }

    public async Task<IReadOnlyList<GroupMarketView>> ReadCaseAsync(Subject subject, Guid caseId, CancellationToken cancellationToken)
    {
        var effective = await access.ResolveAsync(subject, cancellationToken);
        if (!effective.CanReadProcurement) throw new AccessDeniedException();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (!await ProcurementVisibility.ApplyRead(db.PropertyCases.AsNoTracking(), db, effective).AnyAsync(item => item.Id == caseId, cancellationToken))
            throw new AccessDeniedException();
        Guid[] ids = await CaseGroups(db, effective.OrganizationId).Where(item => item.CaseId == caseId)
            .Select(item => item.GroupId).Distinct().ToArrayAsync(cancellationToken);
        return await ReadCoreAsync(db, effective, ids, cancellationToken);
    }

    internal sealed class CaseGroup
    {
        public Guid CaseId { get; init; }
        public Guid GroupId { get; init; }
    }
    // Только подтверждённые источники и фактические наблюдения. ListingId карточки не является связью группы.
    internal static IQueryable<CaseGroup> CaseGroups(LandErpDbContext db, Guid organizationId) =>
        from link in db.PropertyCaseSourceLinks.AsNoTracking()
        join listing in db.Listings.AsNoTracking() on link.CatalogItemId equals listing.Id
        join observation in db.ListingObservations.AsNoTracking() on listing.Id equals observation.ListingId
        join job in db.CollectionJobs.AsNoTracking() on observation.JobId equals job.Id
        join search in db.SearchConfigurations.AsNoTracking() on job.SearchId equals search.Id
        join groupItem in db.SearchGroups.AsNoTracking() on search.SearchGroupId equals groupItem.Id
        where link.OrganizationId == organizationId && link.Confirmed && listing.OrganizationId == organizationId
            && job.OrganizationId == organizationId && search.OrganizationId == organizationId
            && groupItem.OrganizationId == organizationId && groupItem.Active
        select new CaseGroup { CaseId = link.PropertyCaseId, GroupId = groupItem.Id };

    internal static async Task<GroupMarketView[]> ReadCoreAsync(LandErpDbContext db, EffectiveEmployeeAccess effective,
        Guid[] groupIds, CancellationToken cancellationToken)
    {
        var groups = await db.SearchGroups.AsNoTracking()
            .Where(item => item.OrganizationId == effective.OrganizationId && item.Active && groupIds.Contains(item.Id))
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
        var settings = await db.SearchGroupMarketSettings.AsNoTracking()
            .Where(item => item.OrganizationId == effective.OrganizationId && groupIds.Contains(item.SearchGroupId))
            .ToDictionaryAsync(item => item.SearchGroupId, cancellationToken);
        var participants = await CatalogMarketParticipants.ReadAsync(db, effective.OrganizationId, groupIds, cancellationToken);
        var totals = participants.GroupBy(item => item.GroupId).ToDictionary(group => group.Key, group =>
        {
            decimal[] prices = group.Select(item => item.PricePerSotka).Order().ToArray();
            int middle = prices.Length / 2;
            decimal median = prices.Length % 2 == 1 ? prices[middle] : (prices[middle - 1] + prices[middle]) / 2m;
            return (Median: CatalogMarketParticipants.Round(median), Average: CatalogMarketParticipants.Round(prices.Average()), Count: prices.Length);
        });
        return groups.Select(group =>
        {
            var value = settings.GetValueOrDefault(group.Id);
            var total = totals.GetValueOrDefault(group.Id);
            return new GroupMarketView(group.Id, group.Name, total.Count == 0 ? null : total.Median,
                total.Count == 0 ? null : total.Average, total.Count, value?.DemandTestPricePerSotka,
                value?.Version ?? 0, effective.CanHeadProcurement, effective.CanReadIncoming);
        }).ToArray();
    }

    public async Task SaveDemandAsync(Subject subject, SaveDemandTestPrice command, string correlationId, CancellationToken cancellationToken)
    {
        var effective = await access.ResolveAsync(subject, cancellationToken);
        if (!effective.CanHeadProcurement) throw new AccessDeniedException();
        decimal? price = command.PricePerSotka is decimal raw ? decimal.Round(raw, 2, MidpointRounding.ToEven) : null;
        if (price is <= 0 or >= 1000000000000000m) throw new ArgumentException("Укажите положительную цену в рублях или очистите поле.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Блокировка родителя закрывает гонку двух первых записей при отсутствии настроек.
        var group = await db.SearchGroups.FromSqlInterpolated($"SELECT * FROM collection.search_groups WHERE id = {command.SearchGroupId} AND organization_id = {effective.OrganizationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
        if (!group.Active) throw new AccessDeniedException();
        var settings = await db.SearchGroupMarketSettings.SingleOrDefaultAsync(item => item.OrganizationId == effective.OrganizationId
            && item.SearchGroupId == group.Id, cancellationToken);
        if ((settings?.Version ?? 0) != command.ExpectedVersion) throw new DbUpdateConcurrencyException();
        if (settings == null)
        {
            settings = new SearchGroupMarketSettings { Id = DataConventions.NewId(), OrganizationId = effective.OrganizationId, SearchGroupId = group.Id };
            db.SearchGroupMarketSettings.Add(settings);
        }
        decimal? previous = settings.DemandTestPricePerSotka;
        settings.DemandTestPricePerSotka = price;
        OrganizationWorkspace.AddAudit(db, effective.OrganizationContext, subject, "DemandTestPriceChanged", "SearchGroup", group.Id,
            new { PreviousPricePerSotka = previous, PricePerSotka = price, Currency = "RUB", Meaning = "Ручной ориентир теста спроса" }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
