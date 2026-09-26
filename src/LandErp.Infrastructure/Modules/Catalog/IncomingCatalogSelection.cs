using System.Linq.Expressions;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static LandErp.Infrastructure.Modules.Catalog.IncomingCatalogQuery;

namespace LandErp.Infrastructure.Modules.Catalog;

// The page and bulk preview share this exact selection, including archive normalization.
internal sealed record IncomingCatalogSelection(IncomingCatalogReadFilter Filter, IncomingCatalogQuery Predicates,
    IReadOnlyList<IncomingFilterPresetView> Presets, IncomingFilterPresetView[] Applicable,
    Expression<Func<Listing, bool>> Union, Expression<Func<Listing, bool>> Current,
    Func<IncomingCatalogPreset?, Expression<Func<Listing, bool>>> ContextFor,
    IQueryable<Guid> ReviewedIds, IQueryable<Guid> PendingDuplicateListingIds, int? ParticipantTotal)
{
    internal static async Task<IncomingCatalogSelection> PrepareAsync(LandErpDbContext db, Guid organizationId,
        DateTimeOffset now, IncomingCatalogReadFilter filter, IReadOnlyList<IncomingFilterPresetView> presets,
        CancellationToken cancellationToken)
    {
        bool participants = filter.WorkingScope?.Mode == IncomingCatalogMode.Participants;
        Guid[] participantIds = [];
        if (participants)
        {
            if (filter.SearchGroupId is not Guid participantGroup) throw new ArgumentException("Укажите группу участников.");
            participantIds = (await CatalogMarketParticipants.ReadAsync(db, organizationId, [participantGroup], cancellationToken))
                .Select(item => item.ListingId).ToArray();
            // Only the explicit text search and sorting can narrow this dedicated mode.
            filter = new(new(filter.Base.Text, Disposition: null, Offset: filter.Base.Offset, Size: filter.Base.Size),
                SortField: filter.SortField, SortDirection: filter.SortDirection, SearchGroupId: participantGroup,
                WorkingScope: new(IncomingCatalogMode.Participants));
            presets = [];
        }
        bool archive = filter.WorkingScope?.Mode == IncomingCatalogMode.Archive;
        if (archive)
        {
            CatalogDisposition? state = filter.WorkingScope!.ArchiveState;
            if (state != null && !IncomingCatalogArchive.States.Contains(state.Value))
                throw new ArgumentException("Недопустимое состояние архива.");
            // Archive is a separate view, not a saved-filter intersection. Only visible text,
            // group and ordering survive entry; stale copied price/land/state criteria cannot hide it.
            filter = new(new(filter.Base.Text, Disposition: state, Offset: filter.Base.Offset, Size: filter.Base.Size),
                SortField: filter.SortField, SortDirection: filter.SortDirection, SearchGroupId: filter.SearchGroupId,
                WorkingScope: new(IncomingCatalogMode.Archive, ArchiveState: state));
        }
        IncomingCatalogFilter baseFilter = filter.Base;
        IncomingCatalogReadService.Validate(filter);

        if (filter.WorkingScope is not { Mode: IncomingCatalogMode.SavedFilters, SelectedPresetId: not null, UseDraft: false }
            && filter.SearchGroupId is Guid requestedGroup && !await db.SearchGroups.AsNoTracking()
            .AnyAsync(group => group.Id == requestedGroup && group.OrganizationId == organizationId
                && group.Active, cancellationToken))
            throw new ArgumentException("Группа поиска не найдена или недоступна.");
        IQueryable<Listing> organizationItems = db.Listings.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId);

        (DateTimeOffset businessDayStart, DateTimeOffset businessDayEnd) = IncomingCatalogReadService.BusinessDayUtc(now);

        IQueryable<Guid> priceChangedIds = db.CatalogEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.Kind == CatalogEventKind.SourceChanged
                && ((item.PreviousObservedPrice != null && item.ObservedPrice != null
                        && item.PreviousObservedPrice != item.ObservedPrice)
                    || item.Message.Contains("цена")))
            .Select(item => item.CatalogItemId).Distinct();
        IQueryable<Guid> returnedFromMonitoringIds = db.CatalogEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.Kind == CatalogEventKind.MonitoringTriggered)
            .Select(item => item.CatalogItemId).Distinct();
        IQueryable<Guid> reviewedIds = db.CatalogEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && (item.Kind == CatalogEventKind.ReviewStarted
                    || item.Kind == CatalogEventKind.Classified
                    || item.Kind == CatalogEventKind.MonitoringStarted
                    || item.Kind == CatalogEventKind.CaseResumed))
            .Select(item => item.CatalogItemId)
            .Union(db.PropertyCaseSourceLinks.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && item.Confirmed)
                .Select(item => item.CatalogItemId))
            .Distinct();
        IQueryable<Guid> pendingDuplicateListingIds = db.CatalogDuplicateCandidates.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.Status == DuplicateCandidateStatus.Pending)
            .Select(item => item.ListingId).Distinct();
        IQueryable<Guid> processedTodayIds = db.CatalogEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.RecordedAt >= businessDayStart && item.RecordedAt < businessDayEnd
                && (item.Kind == CatalogEventKind.Classified
                    || item.Kind == CatalogEventKind.MonitoringStarted
                    || item.Kind == CatalogEventKind.CaseResumed))
            .Select(item => item.CatalogItemId)
            .Union(db.PropertyCaseSourceLinks.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && item.Confirmed
                    && item.RecordedAt >= businessDayStart && item.RecordedAt < businessDayEnd)
                .Select(item => item.CatalogItemId))
            .Distinct();

        IncomingCatalogQuery predicates = new(db, organizationId, now, priceChangedIds,
            returnedFromMonitoringIds, reviewedIds, pendingDuplicateListingIds, processedTodayIds);
        IncomingCatalogWorkingScope? scope = filter.WorkingScope;
        if (scope != null && (!Enum.IsDefined(scope.Mode) || (scope.Slice != null && !Enum.IsDefined(scope.Slice.Value))))
            throw new ArgumentException("Некорректный режим входящих.");
        var applicable = presets.Where(item => item.CompatibilityIssue == null).ToArray();
        var savedConditions = applicable.ToDictionary(item => item.Id,
            item => predicates.Conditions(FromCriteria(item.Criteria)));
        Expression<Func<Listing, bool>> union = item => false;
        foreach (var condition in savedConditions.Values) union = Or(union, condition);
        IncomingFilterPresetView? selected = scope?.Mode == IncomingCatalogMode.SavedFilters && scope.SelectedPresetId != null
            ? presets.FirstOrDefault(item => item.Id == scope.SelectedPresetId) : null;
        bool hasSelection = scope?.Mode == IncomingCatalogMode.SavedFilters && scope.SelectedPresetId != null;
        Expression<Func<Listing, bool>> selection = scope?.Mode == IncomingCatalogMode.SavedFilters ? union : item => true;
        if (participants) selection = item => participantIds.Contains(item.Id);
        if (archive)
        {
            CatalogDisposition[] archiveStates = IncomingCatalogArchive.States.ToArray();
            selection = item => archiveStates.Contains(item.Disposition);
        }
        IncomingCatalogReadFilter transient = filter;
        if (hasSelection)
        {
            selection = selected?.CompatibilityIssue == null && selected != null
                ? predicates.Conditions(scope!.UseDraft ? filter : FromCriteria(selected.Criteria))
                : item => false;
            // A specific preset replaces the previous preset/group/conditions. Drafts are complete
            // local replacements, never an intersection with the persisted copy (F-01 editing).
            transient = new(new(filter.Base.Text, Disposition: null));
        }
        Expression<Func<Listing, bool>> ContextFor(IncomingCatalogPreset? slice)
        {
            var conditions = slice == null ? transient : transient with
            {
                Base = transient.Base with { Disposition = null }, Preset = null
            };
            return And(And(selection, predicates.Conditions(conditions)), predicates.Slice(slice));
        }
        IncomingCatalogPreset? activeSlice = scope?.Slice;
        Expression<Func<Listing, bool>> current = scope == null ? predicates.Conditions(filter) : ContextFor(activeSlice);
        return new(filter, predicates, presets, applicable, union, current, ContextFor, reviewedIds, pendingDuplicateListingIds, participants ? participantIds.Length : null);
    }
}
