using LandErp.Application.Foundation;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Domain;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;
using System.Text.Json;

namespace LandErp.Infrastructure.Modules.Procurement;

/// <summary>Catalog is organization-shared; procurement access is derived only from case responsibility.</summary>
public sealed class ProcurementWorkspace(IDbContextFactory<LandErpDbContext> factory, IAccessControl access, TimeProvider time, IFileStorage fileStorage)
    : IProcurementWorkspace, ICatalogWorkspace
{
    private sealed class Row
    {
        public PropertyCase Case { get; init; } = default!;
        public Assignment Assignment { get; init; } = default!;
        public WorkTask Task { get; init; } = default!;
    }

    private static IQueryable<Row> VisibleCases(LandErpDbContext db, AccessContext context)
    {
        IQueryable<PropertyCase> cases = ProcurementVisibility.Apply(db.PropertyCases, db, context);
        return from item in cases
                   join assignment in db.WorkAssignments on item.AssignmentId equals assignment.Id
                   join task in db.WorkTasks on item.WorkTaskId equals task.Id
                   select new Row { Case = item, Assignment = assignment, Task = task };
    }

    public async Task<IncomingCatalogPage> ReadIncomingAsync(Subject subject, IncomingCatalogFilter filter, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        ValidatePage(filter.Text, filter.Offset, filter.Size);
        if (!Enum.IsDefined(filter.Age) || filter.MinPrice < 0 || filter.MaxPrice < 0 || filter.MinAreaSquareMeters < 0 || filter.MaxAreaSquareMeters < 0
            || filter.MinPrice > filter.MaxPrice || filter.MinAreaSquareMeters > filter.MaxAreaSquareMeters)
            throw new ArgumentException("Некорректный диапазон фильтра входящих.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        IQueryable<Listing> organizationItems = db.Listings.Where(item => item.OrganizationId == context.OrganizationId);
        IncomingCatalogSummary summary = new(
            await organizationItems.CountAsync(item => item.Disposition == CatalogDisposition.Incoming, cancellationToken),
            await organizationItems.CountAsync(item => item.AttentionRequired, cancellationToken),
            await organizationItems.CountAsync(item => item.Disposition == CatalogDisposition.Monitoring, cancellationToken),
            await organizationItems.CountAsync(item => item.Disposition == CatalogDisposition.InWork, cancellationToken),
            await organizationItems.CountAsync(item => item.Price == null || item.AreaSquareMeters == null || item.Location == null, cancellationToken));
        IQueryable<Listing> query = organizationItems;
        if (filter.Text.Length > 0)
            query = query.Where(item => (item.Title ?? "").Contains(filter.Text) || (item.Location ?? "").Contains(filter.Text)
                || (item.ExternalId ?? "").Contains(filter.Text) || (item.CadastralNumber ?? "").Contains(filter.Text));
        if (filter.Source != null) query = query.Where(item => item.Source == filter.Source);
        if (filter.Disposition != null) query = query.Where(item => item.Disposition == filter.Disposition);
        if (filter.AttentionOnly) query = query.Where(item => item.AttentionRequired);
        if (filter.MinPrice != null) query = query.Where(item => item.Price >= filter.MinPrice);
        if (filter.MaxPrice != null) query = query.Where(item => item.Price <= filter.MaxPrice);
        if (filter.MinAreaSquareMeters != null) query = query.Where(item => item.AreaSquareMeters >= filter.MinAreaSquareMeters);
        if (filter.MaxAreaSquareMeters != null) query = query.Where(item => item.AreaSquareMeters <= filter.MaxAreaSquareMeters);
        DateTimeOffset now = time.GetUtcNow();
        query = filter.Age switch
        {
            CatalogAgeRange.Today => query.Where(item => item.ReceivedAt >= now.AddDays(-1)),
            CatalogAgeRange.ThreeDays => query.Where(item => item.ReceivedAt >= now.AddDays(-3)),
            CatalogAgeRange.Week => query.Where(item => item.ReceivedAt >= now.AddDays(-7)),
            CatalogAgeRange.OlderThanWeek => query.Where(item => item.ReceivedAt < now.AddDays(-7)),
            _ => query
        };
        int total = await query.CountAsync(cancellationToken);
        Listing[] items = await query.OrderByDescending(item => item.ChangedAt).ThenBy(item => item.Id)
            .Skip(filter.Offset).Take(filter.Size).ToArrayAsync(cancellationToken);
        Guid[] ids = items.Select(item => item.Id).ToArray();
        var linked = await (from link in db.PropertyCaseSourceLinks
                            join propertyCase in db.PropertyCases on link.PropertyCaseId equals propertyCase.Id
                            where ids.Contains(link.CatalogItemId) && link.Confirmed
                            select new { link.CatalogItemId, propertyCase.Id, propertyCase.BusinessNumber, propertyCase.StageId }).ToArrayAsync(cancellationToken);
        var byItem = linked.ToDictionary(item => item.CatalogItemId);
        Guid[] groupIds = items.Where(item => item.ObjectGroupId != null).Select(item => item.ObjectGroupId!.Value)
            .Distinct().ToArray();
        Dictionary<Guid, int> groupCounts = groupIds.Length == 0
            ? []
            : await db.Listings.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId
                    && item.ObjectGroupId != null && groupIds.Contains(item.ObjectGroupId.Value))
                .GroupBy(item => item.ObjectGroupId!.Value)
                .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);
        return new(items.Select(item =>
        {
            byItem.TryGetValue(item.Id, out var link);
            int groupCount = item.ObjectGroupId is Guid groupId && groupCounts.TryGetValue(groupId, out int count) ? count : 0;
            return CatalogView(item, link?.Id, link?.BusinessNumber, link?.StageId, groupCount);
        }).ToArray(), total, summary);
    }

    public async Task<CatalogItemDetail> ReadItemAsync(Subject subject, Guid catalogItemId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Listing item = await db.Listings.AsNoTracking().SingleOrDefaultAsync(value => value.Id == catalogItemId
            && value.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        var link = await (from sourceLink in db.PropertyCaseSourceLinks
                          join propertyCase in db.PropertyCases on sourceLink.PropertyCaseId equals propertyCase.Id
                          where sourceLink.CatalogItemId == item.Id && sourceLink.Confirmed
                          select new { propertyCase.Id, propertyCase.BusinessNumber, propertyCase.StageId }).SingleOrDefaultAsync(cancellationToken);
        int objectGroupMemberCount = item.ObjectGroupId == null ? 0 : await db.Listings.AsNoTracking()
            .CountAsync(value => value.OrganizationId == context.OrganizationId
                && value.ObjectGroupId == item.ObjectGroupId, cancellationToken);
        CatalogEventView[] events = await db.CatalogEvents.AsNoTracking().Where(value => value.CatalogItemId == item.Id)
            .OrderByDescending(value => value.RecordedAt).ThenByDescending(value => value.Id).Take(100)
            .Select(value => new CatalogEventView(value.Id, value.Kind, value.Message,
                value.PreviousObservedPrice, value.ObservedPrice,
                value.PreviousObservedPricePerSotka, value.ObservedPricePerSotka,
                value.RecordedAt)).ToArrayAsync(cancellationToken);
        return new(CatalogView(item, link?.Id, link?.BusinessNumber, link?.StageId, objectGroupMemberCount), item.SellerName, item.IngressComment,
            new(item.TargetTotalPrice, item.TargetPricePerSotka, item.MonitoringStartedAt, item.LastEvaluatedPrice,
                item.LastEvaluatedPricePerSotka, item.LastEvaluatedAt), events);
    }

    public async Task RegisterViewAsync(Subject subject, Guid catalogItemId, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Listing item = (await db.Listings.FromSqlInterpolated(
            $"SELECT * FROM catalog.listings WHERE id={catalogItemId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw new AccessDeniedException();

        DateTimeOffset now = time.GetUtcNow();
        bool firstView = !await db.CatalogEvents.AnyAsync(value => value.OrganizationId == context.OrganizationId
            && value.CatalogItemId == item.Id && value.Kind == CatalogEventKind.ReviewStarted, cancellationToken);
        if (firstView)
            db.CatalogEvents.Add(CatalogEvent(item, CatalogEventKind.ReviewStarted, "Предложение впервые просмотрено", now));

        OrganizationWorkspace.AddAudit(db, context, subject, "CatalogItemViewed", "CatalogItem", item.Id,
            new { FirstView = firstView }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid> CreateManualAsync(Subject subject, CreateManualCatalogItem command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        if (command.Source is CatalogSource.Avito or CatalogSource.Cian)
            throw new ArgumentException("Автоматические marketplace-источники поступают через Collector.");
        string title = Required(command.Title, 3, 20000, "Укажите название предложения.");
        string comment = Required(command.Comment, 3, 4000, "Укажите происхождение или комментарий.");
        string? url = Optional(command.Url, 2000);
        if (url != null && (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) || parsed.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Ссылка должна использовать HTTPS.");
        string? externalId = Optional(command.ExternalId, 512);
        DateTimeOffset now = time.GetUtcNow();
        Listing item = new()
        {
            Id = DataConventions.NewId(),
            OrganizationId = context.OrganizationId,
            Source = command.Source,
            ExternalId = externalId,
            Url = url,
            Title = title,
            Location = Optional(command.Location, 20000),
            Price = command.Price == null ? null : DataConventions.RoundRubles(command.Price.Value),
            Currency = "RUB",
            AreaSquareMeters = command.AreaSquareMeters == null ? null : decimal.Round(command.AreaSquareMeters.Value, 4, MidpointRounding.ToEven),
            CadastralNumber = Optional(command.CadastralNumber, 128),
            Description = Optional(command.Description, 20000),
            IngestionKind = CatalogIngestionKind.Employee,
            CreatedByEmployeeId = context.EmployeeId,
            Provenance = "Добавлено сотрудником",
            IngressComment = comment,
            Disposition = CatalogDisposition.Incoming,
            ReceivedAt = now,
            RecordedAt = now,
            ChangedAt = now,
            QueueReason = "Добавлено вручную",
            AttentionRequired = true,
            AttentionAt = now
        };
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        IncomingDuplicateDetector.ApplyExtractedCadastral(item);
        db.Listings.Add(item);
        await IncomingDuplicateDetector.RefreshAsync(db, item, now, cancellationToken);
        OrganizationWorkspace.AddAudit(db, context, subject, "CatalogItemCreatedManually", "CatalogItem", item.Id,
            new { item.Source, item.ExternalId, HasUrl = item.Url != null, item.CadastralNumber }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return item.Id;
    }

    public async Task SetDispositionAsync(Subject subject, SetCatalogDisposition command, string correlationId, CancellationToken cancellationToken)
    {
        if (command.Disposition is CatalogDisposition.InWork or CatalogDisposition.Monitoring
            or CatalogDisposition.Incoming or CatalogDisposition.Duplicate)
            throw new ArgumentException("Используйте специальное действие для выбранного состояния.");
        if (!Enum.IsDefined(command.Disposition)) throw new ArgumentException("Классификация не поддерживается.");
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Listing item = await db.Listings.SingleOrDefaultAsync(value => value.Id == command.CatalogItemId && value.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (item.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException();
        string reason = Required(command.Reason, 3, 4000, "Укажите причину решения.");
        item.Disposition = command.Disposition;
        item.QueueReason = reason;
        item.AttentionRequired = false;
        item.TargetTotalPrice = null;
        item.TargetPricePerSotka = null;
        item.MonitoringStartedAt = null;
        db.Entry(item).Property(value => value.Version).IsModified = true;
        db.CatalogEvents.Add(CatalogEvent(item, CatalogEventKind.Classified,
            $"{DispositionLabel(command.Disposition)}: {reason}", time.GetUtcNow()));
        OrganizationWorkspace.AddAudit(db, context, subject, "CatalogDispositionChanged", "CatalogItem", item.Id,
            new { command.Disposition, Reason = reason }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetMonitoringAsync(Subject subject, SetCatalogMonitoring command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        decimal? total = command.TargetTotalPrice == null ? null : DataConventions.RoundRubles(command.TargetTotalPrice.Value);
        decimal? perSotka = command.TargetPricePerSotka == null ? null : DataConventions.RoundRubles(command.TargetPricePerSotka.Value);
        if (total <= 0 || perSotka <= 0 || total == null && perSotka == null)
            throw new ArgumentException("Укажите положительную целевую общую цену или цену за сотку.");
        string reason = Required(command.Reason, 3, 4000, "Укажите комментарий к мониторингу.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Listing item = await db.Listings.SingleOrDefaultAsync(value => value.Id == command.CatalogItemId
            && value.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (item.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException();
        DateTimeOffset now = time.GetUtcNow();
        item.Disposition = CatalogDisposition.Monitoring;
        item.TargetTotalPrice = total;
        item.TargetPricePerSotka = perSotka;
        item.MonitoringStartedAt = now;
        item.LastEvaluatedPrice = item.Price;
        item.LastEvaluatedPricePerSotka = PricePerSotka(item.Price, item.AreaSquareMeters);
        item.LastEvaluatedAt = now;
        item.AttentionRequired = false;
        item.QueueReason = reason;
        db.Entry(item).Property(value => value.Version).IsModified = true;
        db.CatalogEvents.Add(CatalogEvent(item, CatalogEventKind.MonitoringStarted,
            $"Мониторинг цены: {reason}", now));
        CatalogMonitoringEvaluator.Evaluate(db, item, now);
        OrganizationWorkspace.AddAudit(db, context, subject, "CatalogMonitoringStarted", "CatalogItem", item.Id,
            new { TargetTotalPrice = total, TargetPricePerSotka = perSotka, Reason = reason }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReviewDuplicateCandidateAsync(Subject subject, ReviewCatalogDuplicateCandidate command,
        string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);

        CatalogDuplicateCandidate candidate = (await db.CatalogDuplicateCandidates.FromSqlInterpolated(
            $"SELECT * FROM catalog.duplicate_candidates WHERE id={command.CandidateId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw new AccessDeniedException();
        if (candidate.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException();
        if (candidate.Status != DuplicateCandidateStatus.Pending)
            throw new ArgumentException("Кандидат на дубль уже обработан.");

        Listing listing = await db.Listings.SingleOrDefaultAsync(item =>
            item.Id == candidate.ListingId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        Listing other = await db.Listings.SingleOrDefaultAsync(item =>
            item.Id == candidate.CandidateListingId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();

        DateTimeOffset now = time.GetUtcNow();
        Guid? objectGroupId = listing.ObjectGroupId;
        if (command.Confirmed)
        {
            if (listing.Disposition is not (CatalogDisposition.Incoming or CatalogDisposition.Duplicate))
                throw new ArgumentException("Подтвердить совпадение можно только для входящего предложения или уже связанного дубля.");
            objectGroupId = await LinkSameObjectAsync(db, context.OrganizationId, listing, other, now, cancellationToken);
            listing.Disposition = CatalogDisposition.Duplicate;
            listing.QueueReason = $"Тот же объект: {other.Title ?? other.Location ?? other.Id.ToString()}";
            listing.AttentionRequired = false;
            listing.AttentionAt = null;
            listing.ChangedAt = now;
            db.Entry(listing).Property(value => value.Version).IsModified = true;
            db.CatalogEvents.Add(CatalogEvent(listing, CatalogEventKind.Classified,
                $"Связано в группу одного объекта с {other.Title ?? other.Location ?? other.Id.ToString()}", now));
        }

        candidate.Status = command.Confirmed ? DuplicateCandidateStatus.Confirmed : DuplicateCandidateStatus.Rejected;
        candidate.ReviewedByEmployeeId = context.EmployeeId;
        candidate.ReviewedAt = now;
        candidate.UpdatedAt = now;

        OrganizationWorkspace.AddAudit(db, context, subject,
            command.Confirmed ? "CatalogDuplicateConfirmed" : "CatalogDuplicateRejected",
            "CatalogItem", listing.Id,
            new { CandidateId = candidate.Id, OtherCatalogItemId = other.Id, candidate.Score, ObjectGroupId = objectGroupId }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task LinkCatalogItemsAsSameObjectAsync(Subject subject, LinkCatalogItemsAsSameObject command,
        string correlationId, CancellationToken cancellationToken)
    {
        if (command.CatalogItemId == command.OtherCatalogItemId)
            throw new ArgumentException("Нельзя связать объявление само с собой.");
        string reason = Required(command.Reason, 3, 4000, "Укажите причину связи.");
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);

        Listing listing = await db.Listings.SingleOrDefaultAsync(item =>
            item.Id == command.CatalogItemId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (listing.Version != command.ExpectedCatalogVersion) throw new DbUpdateConcurrencyException();
        Listing other = await db.Listings.SingleOrDefaultAsync(item =>
            item.Id == command.OtherCatalogItemId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (listing.Disposition == CatalogDisposition.Fake || other.Disposition == CatalogDisposition.Fake)
            throw new ArgumentException("Фейковое предложение нельзя включить в группу одного объекта.");
        if (listing.ObjectGroupId != null && listing.ObjectGroupId == other.ObjectGroupId)
            throw new ArgumentException("Объявления уже находятся в одной группе объекта.");

        DateTimeOffset now = time.GetUtcNow();
        Guid objectGroupId = await LinkSameObjectAsync(db, context.OrganizationId, listing, other, now, cancellationToken);
        CatalogDuplicateCandidate? candidate = await db.CatalogDuplicateCandidates.SingleOrDefaultAsync(item =>
            item.OrganizationId == context.OrganizationId
            && ((item.ListingId == listing.Id && item.CandidateListingId == other.Id)
                || (item.ListingId == other.Id && item.CandidateListingId == listing.Id)), cancellationToken);
        if (candidate == null)
        {
            candidate = new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
                ListingId = listing.Id, CandidateListingId = other.Id, Score = 0,
                ReasonsJson = JsonSerializer.Serialize<string[]>(["Связано менеджером вручную после сравнения"]),
                Status = DuplicateCandidateStatus.Confirmed, ReviewedByEmployeeId = context.EmployeeId,
                RecordedAt = now, UpdatedAt = now, ReviewedAt = now
            };
            db.CatalogDuplicateCandidates.Add(candidate);
        }
        else
        {
            candidate.Status = DuplicateCandidateStatus.Confirmed;
            candidate.ReviewedByEmployeeId = context.EmployeeId;
            candidate.ReviewedAt = now;
            candidate.UpdatedAt = now;
        }

        if (listing.Disposition == CatalogDisposition.Incoming)
        {
            listing.Disposition = CatalogDisposition.Duplicate;
            listing.AttentionRequired = false;
            listing.AttentionAt = null;
        }
        listing.QueueReason = $"Связано в один объект: {reason}";
        listing.ChangedAt = now;
        db.Entry(listing).Property(value => value.Version).IsModified = true;
        db.CatalogEvents.Add(CatalogEvent(listing, CatalogEventKind.Classified,
            $"Связано в группу одного объекта: {reason}", now));
        OrganizationWorkspace.AddAudit(db, context, subject, "CatalogObjectGroupLinked", "CatalogItem", listing.Id,
            new { OtherCatalogItemId = other.Id, ObjectGroupId = objectGroupId, Reason = reason }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UnlinkCatalogItemFromObjectGroupAsync(Subject subject, UnlinkCatalogItemFromObjectGroup command,
        string correlationId, CancellationToken cancellationToken)
    {
        string reason = Required(command.Reason, 3, 4000, "Укажите причину разделения.");
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);

        Listing listing = await db.Listings.SingleOrDefaultAsync(item =>
            item.Id == command.CatalogItemId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (listing.Version != command.ExpectedCatalogVersion) throw new DbUpdateConcurrencyException();
        if (listing.ObjectGroupId is not Guid groupId)
            throw new ArgumentException("Объявление не состоит в группе одного объекта.");

        CatalogObjectGroup group = await db.CatalogObjectGroups.SingleOrDefaultAsync(item =>
            item.Id == groupId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new DbUpdateConcurrencyException();
        Listing[] remaining = await db.Listings.Where(item => item.OrganizationId == context.OrganizationId
                && item.ObjectGroupId == groupId && item.Id != listing.Id)
            .OrderBy(item => item.ReceivedAt).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
        DateTimeOffset now = time.GetUtcNow();

        PropertyCaseSourceLink? sourceLink = await db.PropertyCaseSourceLinks.SingleOrDefaultAsync(item =>
            item.CatalogItemId == listing.Id && item.Confirmed, cancellationToken);
        if (sourceLink != null)
        {
            bool visible = await VisibleCases(db, context).AnyAsync(row => row.Case.Id == sourceLink.PropertyCaseId, cancellationToken);
            if (!visible) throw new AccessDeniedException();
            sourceLink.Confirmed = false;
            db.BusinessTimeline.Add(new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
                ObjectId = sourceLink.PropertyCaseId, ActorEmployeeId = context.EmployeeId, Kind = "SourceUnlinked",
                Title = "Источник исключён из группы одного объекта",
                Body = $"{listing.Source}: {listing.Title ?? "источник"}\nПричина: {reason}", RecordedAt = now
            });
        }

        listing.ObjectGroupId = null;
        if (listing.Disposition is CatalogDisposition.Duplicate or CatalogDisposition.InWork)
            listing.Disposition = CatalogDisposition.Incoming;
        listing.AttentionRequired = true;
        listing.AttentionAt = now;
        listing.QueueReason = $"Исключено из группы объекта: {reason}";
        listing.ChangedAt = now;
        db.Entry(listing).Property(value => value.Version).IsModified = true;

        foreach (Listing other in remaining)
            await RejectSameObjectPairAsync(db, context.EmployeeId, listing, other, now, cancellationToken);

        if (remaining.Length <= 1)
        {
            if (remaining.Length == 1)
            {
                Listing last = remaining[0];
                last.ObjectGroupId = null;
                await PromoteForIndependentReviewAsync(db, last, now, cancellationToken);
                db.Entry(last).Property(value => value.Version).IsModified = true;
            }
            db.CatalogObjectGroups.Remove(group);
        }
        else
        {
            group.UpdatedAt = now;
            db.Entry(group).Property(value => value.Version).IsModified = true;
            Guid[] remainingIds = remaining.Select(item => item.Id).ToArray();
            bool hasCase = await db.PropertyCaseSourceLinks.AnyAsync(link =>
                remainingIds.Contains(link.CatalogItemId) && link.Confirmed, cancellationToken);
            if (!hasCase && remaining.All(item => item.Disposition == CatalogDisposition.Duplicate))
            {
                Listing representative = remaining[0];
                representative.Disposition = CatalogDisposition.Incoming;
                representative.AttentionRequired = true;
                representative.AttentionAt = now;
                representative.QueueReason = "Группа одного объекта требует продолжения обработки после разделения.";
                representative.ChangedAt = now;
                db.Entry(representative).Property(value => value.Version).IsModified = true;
            }
        }

        OrganizationWorkspace.AddAudit(db, context, subject, "CatalogObjectGroupUnlinked", "CatalogItem", listing.Id,
            new { ObjectGroupId = groupId, Reason = reason, RemainingMembers = remaining.Length }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CaseLinkTarget>> ReadLinkTargetsAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await VisibleCases(db, context).OrderByDescending(row => row.Case.RecordedAt).Take(100)
            .Select(row => new CaseLinkTarget(row.Case.Id, row.Case.BusinessNumber, row.Case.WorkingTitle)).ToArrayAsync(cancellationToken);
    }

    public async Task CorrectCaseLinkAsync(Subject subject, CorrectCatalogItemCaseLink command,
        string correlationId, CancellationToken cancellationToken)
    {
        string reason = Required(command.Reason, 3, 4000, "Укажите причину исправления связи.");
        if (command.TargetCaseId == command.ExpectedCaseId)
            throw new ArgumentException("Источник уже связан с выбранным PropertyCase.");

        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Listing catalogItem = (await db.Listings.FromSqlInterpolated(
            $"SELECT * FROM catalog.listings WHERE id={command.CatalogItemId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw new AccessDeniedException();
        if (catalogItem.Version != command.ExpectedCatalogVersion) throw new DbUpdateConcurrencyException();

        PropertyCaseSourceLink currentLink = await db.PropertyCaseSourceLinks.SingleOrDefaultAsync(
            item => item.CatalogItemId == catalogItem.Id && item.Confirmed, cancellationToken)
            ?? throw new DbUpdateConcurrencyException();
        if (currentLink.PropertyCaseId != command.ExpectedCaseId) throw new DbUpdateConcurrencyException();

        Row from = await VisibleCases(db, context).SingleOrDefaultAsync(
            item => item.Case.Id == currentLink.PropertyCaseId, cancellationToken) ?? throw new AccessDeniedException();
        Row? target = null;
        if (command.TargetCaseId is Guid targetCaseId)
        {
            target = await VisibleCases(db, context).SingleOrDefaultAsync(
                item => item.Case.Id == targetCaseId, cancellationToken) ?? throw new AccessDeniedException();
        }

        currentLink.Confirmed = false;
        await db.SaveChangesAsync(cancellationToken);

        DateTimeOffset now = time.GetUtcNow();
        if (target != null)
        {
            PropertyCaseSourceLink? targetLink = await db.PropertyCaseSourceLinks.SingleOrDefaultAsync(
                item => item.CatalogItemId == catalogItem.Id && item.PropertyCaseId == target.Case.Id, cancellationToken);
            if (targetLink == null)
            {
                db.PropertyCaseSourceLinks.Add(new()
                {
                    Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, PropertyCaseId = target.Case.Id,
                    CatalogItemId = catalogItem.Id, Confirmed = true, RelationType = "Source",
                    ActorEmployeeId = context.EmployeeId, Provenance = "Correction relink",
                    ReviewedDataRevision = catalogItem.DataRevision, RecordedAt = now
                });
            }
            else
            {
                targetLink.Confirmed = true;
                targetLink.ActorEmployeeId = context.EmployeeId;
                targetLink.Provenance = "Correction relink";
                targetLink.ReviewedDataRevision = catalogItem.DataRevision;
                targetLink.RecordedAt = now;
            }
        }

        catalogItem.Disposition = target == null ? CatalogDisposition.Incoming : CatalogDisposition.InWork;
        catalogItem.AttentionRequired = target == null;
        catalogItem.AttentionAt = target == null ? now : null;
        catalogItem.QueueReason = target == null
            ? "Связь PropertyCase исправлена: требуется повторная привязка."
            : $"Перепривязан к {target.Case.BusinessNumber}.";
        catalogItem.ChangedAt = now;
        db.Entry(catalogItem).Property(item => item.Version).IsModified = true;

        string sourceLabel = $"{catalogItem.Source}: {TimelineFactValue(catalogItem.Title ?? "источник")}";
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
            ObjectId = from.Case.Id, ActorEmployeeId = context.EmployeeId, Kind = "SourceUnlinked",
            Title = "Ошибочная связь источника исправлена",
            Body = $"{sourceLabel}\nПричина: {reason}", RecordedAt = now
        });
        if (target != null)
        {
            db.BusinessTimeline.Add(new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
                ObjectId = target.Case.Id, ActorEmployeeId = context.EmployeeId, Kind = "SourceRelinked",
                Title = "Источник перепривязан",
                Body = $"{sourceLabel}\nИз {from.Case.BusinessNumber}\nПричина: {reason}", RecordedAt = now
            });
        }

        OrganizationWorkspace.AddAudit(db, context, subject, "PropertyCaseSourceLinkCorrected", "PropertyCase", from.Case.Id,
            new
            {
                CatalogItemId = catalogItem.Id,
                FromCaseId = from.Case.Id,
                FromBusinessNumber = from.Case.BusinessNumber,
                ToCaseId = target?.Case.Id,
                ToBusinessNumber = target?.Case.BusinessNumber,
                Reason = reason
            }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ManualPropertyCaseResult> CreateManualCaseAsync(Subject subject, CreateManualPropertyCase command,
        string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        string title = Required(command.Title, 3, 512, "Укажите название объекта от 3 до 512 символов.");
        string? location = Optional(command.Location, 20000);
        string? cadastralNumber = Optional(command.CadastralNumber, 128);
        string comment = Required(command.Comment, 3, 900, "Укажите происхождение или контекст объекта от 3 до 900 символов.");
        if (command.Price is <= 0m) throw new ArgumentException("Цена должна быть больше нуля.");
        if (command.AreaSquareMeters is <= 0m) throw new ArgumentException("Площадь должна быть больше нуля.");

        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await EnsureActiveEmployeeAsync(db, context.EmployeeId, cancellationToken);
        ProcurementCommandReplay replay = await ProcurementCommandReplay.BeginAsync(db, context, subject,
            command.CommandId, "PropertyCaseCreatedManually", command with { CommandId = null }, cancellationToken);
        if (replay.ExistingCaseId is Guid existingId)
        {
            PropertyCase existing = await VisibleCases(db, context).Where(row => row.Case.Id == existingId)
                .Select(row => row.Case).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
            await transaction.CommitAsync(cancellationToken);
            return new(existing.Id, existing.BusinessNumber);
        }
        PropertyCase propertyCase = await CreatePropertyCaseAsync(db, context, title, command.Price, command.AreaSquareMeters,
            location, cadastralNumber, "Прямое создание PropertyCase: " + comment, "CreateManual", cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
            ObjectId = propertyCase.Id, ActorEmployeeId = context.EmployeeId, Kind = "Created",
            Title = "PropertyCase создан вручную", Body = comment, RecordedAt = now
        });
        replay.Record(db, context, subject, propertyCase.Id, propertyCase.Id,
            new { propertyCase.BusinessNumber, Title = title, Location = location, CadastralNumber = cadastralNumber,
                command.Price, command.AreaSquareMeters, Comment = comment }, correlationId, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(propertyCase.Id, propertyCase.BusinessNumber);
    }

    public async Task<TakeToWorkResult> TakeToWorkAsync(Subject subject, TakeCatalogItemToWork command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await EnsureActiveEmployeeAsync(db, context.EmployeeId, cancellationToken);
        Listing catalogItem = (await db.Listings.FromSqlInterpolated(
            $"SELECT * FROM catalog.listings WHERE id={command.CatalogItemId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw new AccessDeniedException();

        Listing[] sources = catalogItem.ObjectGroupId is Guid groupId
            ? await db.Listings.Where(item => item.OrganizationId == context.OrganizationId && item.ObjectGroupId == groupId)
                .OrderBy(item => item.ReceivedAt).ThenBy(item => item.Id).ToArrayAsync(cancellationToken)
            : [catalogItem];
        Guid[] sourceIds = sources.Select(item => item.Id).ToArray();
        PropertyCaseSourceLink[] currentLinks = await db.PropertyCaseSourceLinks
            .Where(item => sourceIds.Contains(item.CatalogItemId) && item.Confirmed).ToArrayAsync(cancellationToken);
        Guid[] linkedCaseIds = currentLinks.Select(item => item.PropertyCaseId).Distinct().ToArray();
        if (linkedCaseIds.Length > 1)
            throw new ArgumentException("Объявления группы уже связаны с разными PropertyCase. Сначала исправьте связи источников.");

        PropertyCase propertyCase;
        bool created;
        if (linkedCaseIds.Length == 1)
        {
            Guid linkedCaseId = linkedCaseIds[0];
            if (command.ExistingCaseId is Guid requestedCaseId && requestedCaseId != linkedCaseId)
                throw new ArgumentException("Группа объекта уже связана с другим PropertyCase.");
            propertyCase = await VisibleCases(db, context).Where(row => row.Case.Id == linkedCaseId)
                .Select(row => row.Case).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
            created = false;
        }
        else if (command.ExistingCaseId == null)
        {
            propertyCase = await CreatePropertyCaseAsync(db, context, catalogItem.Title ?? "Объект без названия",
                catalogItem.Price, catalogItem.AreaSquareMeters, catalogItem.Location, catalogItem.CadastralNumber,
                "Catalog snapshot at case creation", "TakeWork", cancellationToken, catalogItem.Currency, catalogItem.DataRevision);
            created = true;
        }
        else
        {
            Guid existingCaseId = command.ExistingCaseId.Value;
            propertyCase = await VisibleCases(db, context).Where(row => row.Case.Id == existingCaseId)
                .Select(row => row.Case).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
            created = false;
        }

        DateTimeOffset linkedAt = time.GetUtcNow();
        foreach (Listing source in sources)
        {
            PropertyCaseSourceLink? active = currentLinks.SingleOrDefault(item => item.CatalogItemId == source.Id);
            if (active == null)
            {
                PropertyCaseSourceLink? historical = await db.PropertyCaseSourceLinks.SingleOrDefaultAsync(item =>
                    item.CatalogItemId == source.Id && item.PropertyCaseId == propertyCase.Id, cancellationToken);
                if (historical == null)
                {
                    db.PropertyCaseSourceLinks.Add(new()
                    {
                        Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
                        PropertyCaseId = propertyCase.Id, CatalogItemId = source.Id, Confirmed = true,
                        RelationType = "Source", ActorEmployeeId = context.EmployeeId,
                        Provenance = source.Id == catalogItem.Id ? "User confirmed" : "Object group confirmed",
                        ReviewedDataRevision = source.DataRevision, RecordedAt = linkedAt
                    });
                }
                else
                {
                    historical.Confirmed = true;
                    historical.ActorEmployeeId = context.EmployeeId;
                    historical.Provenance = source.Id == catalogItem.Id ? "User confirmed" : "Object group confirmed";
                    historical.ReviewedDataRevision = source.DataRevision;
                    historical.RecordedAt = linkedAt;
                }
            }
            source.Disposition = CatalogDisposition.InWork;
            source.AttentionRequired = false;
            source.AttentionAt = null;
            source.QueueReason = source.Id == catalogItem.Id
                ? $"В работе: {propertyCase.BusinessNumber}."
                : $"Источник группы одного объекта связан с {propertyCase.BusinessNumber}.";
            source.ChangedAt = linkedAt;
            db.Entry(source).Property(value => value.Version).IsModified = true;
        }

        string title = created ? "Взят в работу" : sources.Length > 1 ? "Группа источников добавлена" : "Добавлен источник";
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
            ObjectId = propertyCase.Id, ActorEmployeeId = context.EmployeeId,
            Kind = created ? "Decision" : "SourceLinked", Title = title,
            Body = sources.Length == 1
                ? $"{catalogItem.Source}: {catalogItem.Title ?? "источник"}"
                : $"Связано источников одного объекта: {sources.Length}.",
            RecordedAt = linkedAt
        });
        OrganizationWorkspace.AddAudit(db, context, subject, created ? "CatalogItemTakenToWork" : "CatalogItemLinkedToCase",
            "PropertyCase", propertyCase.Id,
            new { CatalogItemId = catalogItem.Id, CatalogItemIds = sourceIds, catalogItem.ObjectGroupId, propertyCase.BusinessNumber },
            correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(propertyCase.Id, propertyCase.BusinessNumber, created);
    }

    public async Task<TakeToWorkResult> ResumeCaseAsync(Subject subject, ResumeCatalogItemCase command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await EnsureActiveEmployeeAsync(db, context.EmployeeId, cancellationToken);
        Listing catalogItem = (await db.Listings.FromSqlInterpolated(
            $"SELECT * FROM catalog.listings WHERE id={command.CatalogItemId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw new AccessDeniedException();
        if (catalogItem.Version != command.ExpectedCatalogVersion) throw new DbUpdateConcurrencyException();
        PropertyCaseSourceLink link = await db.PropertyCaseSourceLinks.SingleOrDefaultAsync(value => value.CatalogItemId == catalogItem.Id
            && value.Confirmed, cancellationToken) ?? throw new ArgumentException("Входящий элемент ещё не связан с PropertyCase.");
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(value => value.Case.Id == link.PropertyCaseId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (row.Case.StageId is not ("rejected" or "monitor"))
            throw new ArgumentException("Возобновить можно только отклонённый или приостановленный PropertyCase.");
        string from = row.Case.StageId;
        row.Case.StageId = "analysis";
        row.Case.ManagerEmployeeId = context.EmployeeId;
        row.Assignment.EmployeeId = context.EmployeeId;
        row.Task.EmployeeId = context.EmployeeId;
        row.Task.Completed = false;
        row.Task.DueAt = null;
        row.Task.Title = "Повторный анализ";
        db.Entry(row.Case).Property(value => value.Version).IsModified = true;
        catalogItem.Disposition = CatalogDisposition.InWork;
        catalogItem.AttentionRequired = false;
        db.Entry(catalogItem).Property(value => value.Version).IsModified = true;
        DateTimeOffset now = time.GetUtcNow();
        db.WorkflowTransitions.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
            ObjectId = row.Case.Id, FromStageId = from, ToStageId = "analysis", Action = "Resume",
            ActorEmployeeId = context.EmployeeId, ObjectVersion = row.Case.Version + 1, RecordedAt = now
        });
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
            ObjectId = row.Case.Id, ActorEmployeeId = context.EmployeeId, Kind = "Resume",
            Title = "PropertyCase возобновлён", Body = $"Повторный интерес из источника {catalogItem.Source}: {catalogItem.QueueReason}", RecordedAt = now
        });
        db.CatalogEvents.Add(CatalogEvent(catalogItem, CatalogEventKind.CaseResumed,
            $"Возобновлён {row.Case.BusinessNumber}", now));
        OrganizationWorkspace.AddAudit(db, context, subject, "PropertyCaseResumedFromCatalog", "PropertyCase", row.Case.Id,
            new { CatalogItemId = catalogItem.Id, From = from, To = "analysis" }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(row.Case.Id, row.Case.BusinessNumber, false);
    }

    public async Task<ProcurementQueuePage> ReadQueueAsync(Subject subject, QueueFilter filter, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        ValidatePage(filter.Text, filter.Offset, filter.Size);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        IQueryable<Row> query = VisibleCases(db, context);
        if (filter.Text.Length > 0)
            query = query.Where(row => row.Case.WorkingTitle.Contains(filter.Text) || (row.Case.WorkingLocation ?? "").Contains(filter.Text)
                || row.Case.BusinessNumber.Contains(filter.Text) || db.PropertyCaseSourceLinks.Any(link => link.PropertyCaseId == row.Case.Id
                    && db.Listings.Any(item => item.Id == link.CatalogItemId && ((item.ExternalId ?? "").Contains(filter.Text) || (item.Title ?? "").Contains(filter.Text)))));
        if (filter.Source != null)
            query = query.Where(row => db.PropertyCaseSourceLinks.Any(link => link.PropertyCaseId == row.Case.Id && link.Confirmed
                && db.Listings.Any(item => item.Id == link.CatalogItemId && item.Source == filter.Source)));
        if (filter.Stage.Length > 0) query = query.Where(row => row.Case.StageId == filter.Stage);
        else query = query.Where(row => row.Case.StageId != "rejected"
            || db.PropertyCaseSourceLinks.Any(link => link.PropertyCaseId == row.Case.Id && link.Confirmed
                && db.Listings.Any(item => item.Id == link.CatalogItemId && item.DataRevision > link.ReviewedDataRevision)));
        if (filter.ChangedOnly) query = query.Where(row => db.PropertyCaseSourceLinks.Any(link => link.PropertyCaseId == row.Case.Id && link.Confirmed
            && db.Listings.Any(item => item.Id == link.CatalogItemId && item.DataRevision > link.ReviewedDataRevision)));
        int total = await query.CountAsync(cancellationToken);
        Row[] rows = await query.OrderByDescending(row => row.Case.RecordedAt).ThenBy(row => row.Case.Id)
            .Skip(filter.Offset).Take(filter.Size).ToArrayAsync(cancellationToken);
        Dictionary<Guid, string> names = await db.Employees.Where(item => item.OrganizationId == context.OrganizationId)
            .ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
        var sourceMap = await LoadSourcesAsync(db, rows.Select(row => row.Case.Id).ToArray(), cancellationToken);
        return new(rows.Select(row => Item(row, names, sourceMap.GetValueOrDefault(row.Case.Id, []))).ToArray(), total);
    }

    public async Task<CaseCard> ReadCardAsync(Subject subject, Guid caseId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == caseId, cancellationToken) ?? throw new AccessDeniedException();
        Dictionary<Guid, string> names = await db.Employees.Where(item => item.OrganizationId == context.OrganizationId)
            .ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
        List<(PropertyCaseSourceLink Link, Listing Item)> sources = (await LoadSourcesAsync(db, [caseId], cancellationToken)).GetValueOrDefault(caseId, []);
        Guid[] sourceIds = sources.Select(item => item.Item.Id).ToArray();
        BusinessTimelineEntry[] timeline = await db.BusinessTimeline.Where(item => item.OrganizationId == context.OrganizationId && item.ObjectType == "PropertyCase" && item.ObjectId == caseId)
            .OrderByDescending(item => item.RecordedAt).ThenByDescending(item => item.Id).Take(200).ToArrayAsync(cancellationToken);
        CatalogObservation[] observations = await db.ListingObservations.Where(item => sourceIds.Contains(item.ListingId))
            .OrderByDescending(item => item.ObservedAt).ThenByDescending(item => item.Id).Take(200).ToArrayAsync(cancellationToken);
        DecisionTarget[] heads = await TargetsAsync(db, row.Case, row.Assignment.EmployeeId, Permissions.HeadDecide,
            ProcurementRecipientAccess.BecomesCaseAssignee, cancellationToken);
        DecisionTarget[] managers = await TargetsAsync(db, row.Case, row.Assignment.EmployeeId, Permissions.ManagerDecide,
            ProcurementRecipientAccess.BecomesManagerAndCaseAssignee, cancellationToken);
        DecisionTarget[] assignees = await DossierTargetsAsync(db, row.Case, row.Assignment.EmployeeId,
            ProcurementRecipientAccess.CurrentVisibility, cancellationToken);
        bool manager = await AllowedAsync(subject, Permissions.ManagerDecide, cancellationToken) && row.Assignment.EmployeeId == context.EmployeeId
            && row.Case.StageId is not ("pending_head" or "acquired")
            && (row.Case.StageId != "rejected" || SourcesChanged(sources));
        bool head = await AllowedAsync(subject, Permissions.HeadDecide, cancellationToken) && row.Case.StageId == "pending_head"
            && row.Assignment.EmployeeId == context.EmployeeId && row.Case.ManagerEmployeeId != context.EmployeeId;
        bool canManageDossier = await AllowedAsync(subject, Permissions.ManagerDecide, cancellationToken)
            || await AllowedAsync(subject, Permissions.HeadDecide, cancellationToken);
        bool canManageTemplates = canManageDossier;
        bool canManageBlockers = await AllowedAsync(subject, Permissions.HeadDecide, cancellationToken);
        bool canConfirmPurchase = await AllowedAsync(subject, Permissions.PurchaseConfirm, cancellationToken);
        bool canCorrectSourceLinks = await AllowedAsync(subject, Permissions.ManagerDecide, cancellationToken);
        CaseNegotiation[] negotiations = await db.CaseNegotiations.AsNoTracking().Where(item => item.PropertyCaseId == caseId)
            .OrderByDescending(item => item.EffectiveAt).ThenByDescending(item => item.Id).ToArrayAsync(cancellationToken);
        CaseCheck[] checks = await db.CaseChecks.AsNoTracking().Where(item => item.PropertyCaseId == caseId)
            .OrderBy(item => item.Level).ThenBy(item => item.Title).ToArrayAsync(cancellationToken);
        CaseDocumentRequirement[] documentRequirements = await db.CaseDocumentRequirements.AsNoTracking()
            .Where(item => item.PropertyCaseId == caseId).OrderBy(item => item.Title).ToArrayAsync(cancellationToken);
        CaseCheckTemplateItem[] checkTemplates = await EnsureCheckTemplatesAsync(db, context.OrganizationId, cancellationToken);
        SiteInspection? inspection = await db.SiteInspections.AsNoTracking().SingleOrDefaultAsync(item => item.PropertyCaseId == caseId, cancellationToken);
        SiteInspectionItem[] inspectionItems = inspection == null ? [] : await db.SiteInspectionItems.AsNoTracking()
            .Where(item => item.InspectionId == inspection.Id).OrderBy(item => item.SortOrderSnapshot).ToArrayAsync(cancellationToken);
        var attachments = await (from link in db.CaseAttachments.AsNoTracking()
                                 join file in db.StoredFiles.AsNoTracking() on link.StoredFileId equals file.Id
                                 where link.PropertyCaseId == caseId
                                 orderby link.RecordedAt descending
                                 select new { Link = link, File = file }).ToArrayAsync(cancellationToken);
        Listing? primary = sources.OrderBy(item => item.Link.RecordedAt).Select(item => item.Item).FirstOrDefault();
        string[] photos = sources.SelectMany(item => JsonSerializer.Deserialize<string[]>(item.Item.PhotosJson) ?? []).Distinct().ToArray();
        return new(Item(row, names, sources), primary?.Description, primary?.SellerName, photos,
            sources.Select(value => new CaseSourceView(value.Item.Id, value.Item.Source, value.Item.ExternalId, value.Item.Url,
                value.Item.Title ?? "Источник без названия", value.Item.Price, value.Item.AreaSquareMeters, value.Item.Location,
                value.Item.Provenance, value.Item.LastObservedAt)).ToArray(),
            timeline.Select(item => new TimelineItem(item.Id, item.Kind, item.Title, item.Body, names.GetValueOrDefault(item.ActorEmployeeId, "Сотрудник"),
                item.TargetEmployeeId == null ? null : names.GetValueOrDefault(item.TargetEmployeeId.Value, "Сотрудник"), item.RecordedAt, item.EffectiveAt, item.DueAt)).ToArray(),
            observations.Select(item => new ObservationView(item.Id, item.ListingId, item.ObservedAt, item.RecordedAt,
                JsonSerializer.Deserialize<ListingData>(item.PayloadJson, CollectionJson.Options)!, JsonSerializer.Deserialize<string[]>(item.ChangesJson)!)).ToArray(),
            heads.Where(item => item.EmployeeId != context.EmployeeId).ToArray(), managers, assignees,
            row.Case.ManagerEmployeeId, manager, head,
            negotiations.Select(item => new NegotiationView(item.Id, item.SellerPrice, item.BuyerOffer, item.AgreedPrice, item.Currency,
                item.Channel, item.Contact, item.Outcome, item.Conditions, item.Comment, item.NextStep, item.NextStepDueAt,
                names.GetValueOrDefault(item.AuthorEmployeeId, "Сотрудник"), item.EffectiveAt, item.RecordedAt)).ToArray(),
            checks.Select(item => new CheckView(item.Id, item.Level, item.Title, item.Status,
                item.ResponsibleEmployeeId,
                item.ResponsibleEmployeeId == null ? null : names.GetValueOrDefault(item.ResponsibleEmployeeId.Value, "Сотрудник"),
                item.DueAt, item.Cost, item.Currency, item.Result, item.Blocker, item.Version, item.DescriptionSnapshot,
                item.TemplateItemId, item.TemplateItemVersion)).ToArray(),
            attachments.Select(item => new AttachmentView(item.Link.Id, item.Link.OwnerType,
                AttachmentOwnerId(item.Link), item.Link.Kind, item.Link.Label, item.Link.Description,
                AttachmentOwnerLabel(item.Link, negotiations, checks, inspectionItems), item.File.OriginalName, item.File.ContentType, item.File.SizeBytes,
                item.File.Status, item.File.ExternalUrl != null, item.Link.RecordedAt, item.Link.DocumentRequirementId)).ToArray(),
            documentRequirements.Select(item => new DocumentRequirementView(item.Id, item.Code, item.Title, item.Description,
                item.ExpectedSource, item.Status, item.DueAt, item.Note, names.GetValueOrDefault(item.UpdatedByEmployeeId, "Сотрудник"),
                item.UpdatedAt, item.Version, attachments.Where(value => value.Link.DocumentRequirementId == item.Id)
                    .Select(value => value.Link.Id).ToArray())).ToArray(),
            Discrepancies(row.Case, sources),
            checkTemplates.Select(item => new CheckTemplateView(item.Id, item.Title, item.Level, item.Description, item.SortOrder, item.Active, item.Version)).ToArray(),
            inspection == null ? null : new InspectionView(inspection.Id, inspection.Status, inspection.OverallConclusion, inspection.PreliminaryDecision,
                names.GetValueOrDefault(inspection.InspectorEmployeeId, "Сотрудник"), inspection.StartedAt, inspection.CompletedAt, inspection.Version,
                inspectionItems.Select(item => new InspectionItemView(item.Id, item.TemplateItemId, item.TemplateItemVersion, item.TitleSnapshot,
                    item.SortOrderSnapshot, item.AnswerTypeSnapshot, JsonSerializer.Deserialize<string[]>(item.OptionsJsonSnapshot) ?? [],
                    item.UnitSnapshot, item.NormalAnswerSnapshot, item.AllowAttachmentsSnapshot, item.RequiredSnapshot, item.Status,
                    item.Answer, item.Note, item.Version)).ToArray()),
            row.Case.CadastralNumber, row.Case.AcquisitionPrice, row.Case.AcquisitionDate, row.Case.AcquisitionComment,
            canManageDossier, canManageTemplates, canManageBlockers, canConfirmPurchase, canCorrectSourceLinks);
    }

    public async Task<Guid?> ResolveLegacyListingAsync(Subject subject, Guid listingId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Guid? caseId = await db.PropertyCaseSourceLinks.Where(item => item.CatalogItemId == listingId && item.Confirmed)
            .Select(item => (Guid?)item.PropertyCaseId).SingleOrDefaultAsync(cancellationToken);
        if (caseId == null) return null;
        return await VisibleCases(db, context).AnyAsync(item => item.Case.Id == caseId.Value, cancellationToken) ? caseId : throw new AccessDeniedException();
    }

    public async Task DecideAsync(Subject subject, DecisionCommand command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Action)) throw new ArgumentException("DECISION_INVALID");
        bool headAction = command.Action is ProcurementAction.Return or ProcurementAction.Approve;
        AccessContext read = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, read.OrganizationId, cancellationToken);
        await EnsureActiveEmployeeAsync(db, read.EmployeeId, cancellationToken);
        PropertyCase propertyCase = (await db.PropertyCases.FromSqlInterpolated(
            $"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={read.OrganizationId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw new AccessDeniedException();
        Row row = await VisibleCases(db, read).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        List<(PropertyCaseSourceLink Link, Listing Item)> sources = (await LoadSourcesAsync(db, [propertyCase.Id], cancellationToken)).GetValueOrDefault(propertyCase.Id, []);
        long sourceRevision = SourceRevision(sources);
        if (propertyCase.Version != command.ExpectedCaseVersion || sourceRevision != command.ExpectedSourceRevision) throw new DbUpdateConcurrencyException();
        if (propertyCase.StageId == "acquired")
            throw new ArgumentException("Закупка уже завершена. Для купленного объекта доступны только предусмотренные корректировки данных покупки.");
        headAction |= propertyCase.StageId == "pending_head";
        AccessContext context = await access.RequireAsync(subject, headAction ? Permissions.HeadDecide : Permissions.ManagerDecide, cancellationToken);
        if (row.Assignment.EmployeeId != context.EmployeeId || headAction && (propertyCase.StageId != "pending_head" || propertyCase.ManagerEmployeeId == context.EmployeeId)) throw new AccessDeniedException();
        if (!headAction && command.Action is ProcurementAction.Return or ProcurementAction.Approve
            || headAction && command.Action is not (ProcurementAction.Return or ProcurementAction.Approve or ProcurementAction.Monitor or ProcurementAction.Reject)) throw new AccessDeniedException();
        if (propertyCase.StageId is "approved" or "rejected" && !SourcesChanged(sources)) throw new ArgumentException("Решение завершено. Для нового анализа нужны изменившиеся данные.");
        string reason = Required(command.Reason, 3, 4000, "Укажите пояснение от 3 до 4000 символов.");
        string clarification = command.Action is ProcurementAction.Return or ProcurementAction.Clarify
            ? Required(command.Clarification, 3, 4000, "Укажите, что требуется уточнить.") : Optional(command.Clarification, 4000) ?? "";
        if (command.DueAt?.Offset != null && command.DueAt.Value.Offset != TimeSpan.Zero || command.DueAt < time.GetUtcNow() || command.DueAt > time.GetUtcNow().AddYears(2))
            throw new ArgumentException("Укажите будущий срок в UTC.");
        string from = propertyCase.StageId;
        string stage = command.Action switch { ProcurementAction.Monitor => "monitor", ProcurementAction.Clarify => "clarify", ProcurementAction.Reject => "rejected", ProcurementAction.Forward => "pending_head", ProcurementAction.Return => "returned", _ => "negotiation" };
        Guid target = context.EmployeeId;
        if (command.Action == ProcurementAction.Forward)
        {
            target = command.TargetEmployeeId ?? throw new ArgumentException("Выберите руководителя закупки.");
            if (target == context.EmployeeId || !(await TargetsAsync(db, propertyCase, row.Assignment.EmployeeId,
                Permissions.HeadDecide, ProcurementRecipientAccess.BecomesCaseAssignee, cancellationToken))
                .Any(value => value.EmployeeId == target)) throw new AccessDeniedException();
            propertyCase.PendingApprovalId = DataConventions.NewId();
        }
        if (headAction)
        {
            if (command.Action == ProcurementAction.Approve && SourcesChanged(sources)) throw new ArgumentException("Источники изменились после передачи. Верните объект менеджеру для обновления анализа.");
            target = command.Action == ProcurementAction.Return ? command.TargetEmployeeId ?? propertyCase.ManagerEmployeeId : propertyCase.ManagerEmployeeId;
            if (!(await TargetsAsync(db, propertyCase, row.Assignment.EmployeeId,
                Permissions.ManagerDecide, ProcurementRecipientAccess.BecomesManagerAndCaseAssignee, cancellationToken))
                .Any(value => value.EmployeeId == target)) throw new AccessDeniedException();
            db.Approvals.Add(new()
            {
                Id = propertyCase.PendingApprovalId ?? throw new AccessDeniedException(),
                OrganizationId = context.OrganizationId,
                ObjectType = "PropertyCase",
                ObjectId = propertyCase.Id,
                RequesterEmployeeId = propertyCase.ManagerEmployeeId,
                ApproverEmployeeId = context.EmployeeId,
                Outcome = command.Action.ToString(),
                Reason = reason,
                ConsideredDataRevision = sourceRevision,
                ObjectVersion = propertyCase.Version,
                RecordedAt = time.GetUtcNow()
            });
            propertyCase.PendingApprovalId = null;
            if (command.Action == ProcurementAction.Return) propertyCase.ManagerEmployeeId = target;
        }
        propertyCase.StageId = stage; propertyCase.ReviewedDataRevision = sourceRevision;
        foreach (var source in sources) source.Link.ReviewedDataRevision = source.Item.DataRevision;
        db.Entry(propertyCase).Property(value => value.Version).IsModified = true;
        row.Assignment.EmployeeId = target; row.Task.EmployeeId = target; row.Task.DueAt = command.DueAt;
        row.Task.Completed = stage == "rejected";
        row.Task.Title = stage switch { "pending_head" => "Рассмотреть первичный анализ", "returned" => "Исправить / уточнить первичный анализ", "clarify" => "Уточнить данные объекта", "monitor" => "Наблюдать за объектом", "negotiation" => "Переговоры и проверки", "rejected" => "Объект отклонён", _ => "Первичный анализ" };
        string title = ActionLabel(command.Action, headAction);
        db.WorkflowTransitions.Add(new()
        {
            Id = DataConventions.NewId(),
            OrganizationId = context.OrganizationId,
            ObjectType = "PropertyCase",
            ObjectId = propertyCase.Id,
            FromStageId = from,
            ToStageId = stage,
            Action = command.Action.ToString(),
            ActorEmployeeId = context.EmployeeId,
            ObjectVersion = propertyCase.Version + 1,
            RecordedAt = time.GetUtcNow()
        });
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(),
            OrganizationId = context.OrganizationId,
            ObjectType = "PropertyCase",
            ObjectId = propertyCase.Id,
            ActorEmployeeId = context.EmployeeId,
            Kind = "Decision",
            Title = title,
            Body = reason + (clarification.Length == 0 ? "" : "\nУточнить: " + clarification),
            TargetEmployeeId = target,
            DueAt = command.DueAt,
            RecordedAt = time.GetUtcNow()
        });
        if (target != context.EmployeeId) db.Notifications.Add(new()
        {
            Id = DataConventions.NewId(),
            OrganizationId = context.OrganizationId,
            EmployeeId = target,
            ObjectType = "PropertyCase",
            ObjectId = propertyCase.Id,
            Title = propertyCase.BusinessNumber + ": " + title,
            RecordedAt = time.GetUtcNow()
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "Procurement" + command.Action, "PropertyCase", propertyCase.Id,
            new { From = from, To = stage, Target = target, command.DueAt, Reason = reason, Clarification = clarification, SourceRevision = sourceRevision }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddNoteAsync(Subject subject, AddCaseNote command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await RequireDossierPermissionAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // The expected version protects the first write, not a replay of its committed result.
        ProcurementCommandReplay replay = await ProcurementCommandReplay.BeginAsync(db, context, subject,
            command.CommandId, command.Contact ? "SellerContactRecorded" : "CaseNoteAdded",
            command with { CommandId = null, ExpectedCaseVersion = 0 }, cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(value => value.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        if (replay.ExistingResultId != null)
        {
            if (replay.ExistingCaseId != row.Case.Id) throw new AccessDeniedException();
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        if (row.Case.Version != command.ExpectedCaseVersion) throw new DbUpdateConcurrencyException();
        if (command.EffectiveAt?.Offset != null && command.EffectiveAt.Value.Offset != TimeSpan.Zero || command.EffectiveAt > time.GetUtcNow().AddMinutes(5)) throw new ArgumentException("Укажите фактическое время контакта UTC.");
        string text = Required(command.Text, 3, 4000, "Укажите заметку.");
        string result = command.Contact ? Required(command.ContactResult, 3, 4000, "Укажите результат контакта.") : "";
        db.Entry(row.Case).Property(item => item.Version).IsModified = true;
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(),
            OrganizationId = context.OrganizationId,
            ObjectType = "PropertyCase",
            ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId,
            Kind = command.Contact ? "Contact" : "Note",
            Title = command.Contact ? "Контакт с продавцом" : "Рабочая заметка",
            Body = text + (command.Contact ? "\nРезультат: " + result : ""),
            EffectiveAt = command.EffectiveAt,
            RecordedAt = time.GetUtcNow()
        });
        replay.Record(db, context, subject, row.Case.Id, row.Case.Id,
            new { command.Contact, command.EffectiveAt }, correlationId, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddNegotiationAsync(Subject subject, AddNegotiation command, string correlationId, CancellationToken cancellationToken)
        => await AddNegotiationWithIdAsync(subject, command, correlationId, cancellationToken);

    public async Task<Guid> AddNegotiationWithIdAsync(Subject subject, AddNegotiation command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await RequireDossierPermissionAsync(subject, cancellationToken);
        if (command.EffectiveAt.Offset != TimeSpan.Zero || command.EffectiveAt > time.GetUtcNow().AddMinutes(5))
            throw new ArgumentException("Укажите фактическое время контакта UTC.");
        if (command.NextStepDueAt?.Offset != null && command.NextStepDueAt.Value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Срок следующего шага должен быть указан в UTC.");
        decimal? sellerPrice = Price(command.SellerPrice); decimal? buyerOffer = Price(command.BuyerOffer); decimal? agreedPrice = Price(command.AgreedPrice);
        string channel = Optional(command.Channel, 128) ?? ""; string contact = Optional(command.Contact, 512) ?? "";
        string outcome = Optional(command.Outcome, 1000) ?? ""; string conditions = Optional(command.Conditions, 4000) ?? "";
        string comment = Optional(command.Comment, 4000) ?? ""; string nextStep = Optional(command.NextStep, 1000) ?? "";
        if (sellerPrice == null && buyerOffer == null && agreedPrice == null && channel.Length == 0 && contact.Length == 0
            && outcome.Length == 0 && conditions.Length == 0 && comment.Length == 0 && nextStep.Length == 0)
            throw new ArgumentException("Зафиксируйте хотя бы результат, комментарий, следующий шаг или цену.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        ProcurementCommandReplay replay = await ProcurementCommandReplay.BeginAsync(db, context, subject,
            command.CommandId, "CaseNegotiationAdded", command with { CommandId = null, ExpectedCaseVersion = 0 }, cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        if (replay.ExistingResultId is Guid existingId)
        {
            if (replay.ExistingCaseId != row.Case.Id) throw new AccessDeniedException();
            await transaction.CommitAsync(cancellationToken);
            return existingId;
        }
        if (row.Case.StageId is "rejected" or "monitor") throw new ArgumentException("Сначала возобновите PropertyCase.");
        CaseNegotiation negotiation = new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, PropertyCaseId = row.Case.Id,
            SellerPrice = sellerPrice, BuyerOffer = buyerOffer, AgreedPrice = agreedPrice, Currency = "RUB",
            Channel = channel, Contact = contact, Outcome = outcome, Conditions = conditions, Comment = comment,
            NextStep = nextStep, NextStepDueAt = command.NextStepDueAt, AuthorEmployeeId = context.EmployeeId,
            EffectiveAt = command.EffectiveAt, RecordedAt = time.GetUtcNow()
        };
        db.CaseNegotiations.Add(negotiation);
        db.Entry(row.Case).Property(item => item.Version).IsModified = true;
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId, Kind = "Negotiation", Title = NegotiationLabel(negotiation),
            Body = NegotiationBody(negotiation), EffectiveAt = negotiation.EffectiveAt, DueAt = negotiation.NextStepDueAt, RecordedAt = negotiation.RecordedAt
        });
        replay.Record(db, context, subject, row.Case.Id, negotiation.Id,
            new { negotiation.SellerPrice, negotiation.BuyerOffer, negotiation.AgreedPrice, negotiation.EffectiveAt }, correlationId, negotiation.RecordedAt);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return negotiation.Id;
    }

    public async Task SaveCheckAsync(Subject subject, SaveCaseCheck command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Level) || !Enum.IsDefined(command.Status)) throw new ArgumentException("Некорректный тип или статус проверки.");
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await RequireDossierPermissionAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await EnsureActiveEmployeeAsync(db, context.EmployeeId, cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        if (row.Case.Version != command.ExpectedCaseVersion) throw new DbUpdateConcurrencyException();
        if (row.Case.StageId is "rejected" or "monitor") throw new ArgumentException("Сначала возобновите PropertyCase.");
        if (command.Level == CaseCheckLevel.Deep && row.Case.StageId is not ("negotiation" or "approved"))
            throw new ArgumentException("Глубокая проверка доступна после решения руководителя продолжить работу.");
        if (command.ResponsibleEmployeeId != null)
        {
            DecisionTarget[] responsibleTargets = await DossierTargetsAsync(db, row.Case, row.Assignment.EmployeeId,
                ProcurementRecipientAccess.CurrentVisibility, cancellationToken);
            if (!responsibleTargets.Any(item => item.EmployeeId == command.ResponsibleEmployeeId))
                throw new AccessDeniedException();
        }
        if (command.Cost < 0) throw new ArgumentException("Стоимость проверки не может быть отрицательной.");

        CaseCheck? check = command.CheckId == null ? null : await db.CaseChecks.SingleOrDefaultAsync(item => item.Id == command.CheckId
            && item.PropertyCaseId == row.Case.Id, cancellationToken) ?? throw new AccessDeniedException();
        bool blockerChanged = command.Blocker != (check?.Blocker ?? false);
        if (blockerChanged) await access.RequireAsync(subject, Permissions.HeadDecide, cancellationToken);
        if (check != null && check.Version != command.ExpectedCheckVersion) throw new DbUpdateConcurrencyException();
        DateTimeOffset now = time.GetUtcNow();
        if (command.DueAt?.Offset != null && command.DueAt.Value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Срок проверки должен быть указан в UTC.");
        if (command.DueAt is DateTimeOffset due && due < now && (check == null || check.DueAt != due))
            throw new ArgumentException("Новый или изменённый срок проверки должен быть в будущем.");
        CaseCheckTemplateItem? template = command.TemplateItemId == null ? null : await db.CaseCheckTemplateItems.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.TemplateItemId && item.OrganizationId == context.OrganizationId && item.Active, cancellationToken)
            ?? throw new AccessDeniedException();
        string title = Required(template?.Title ?? command.Title, 3, 512, "Укажите название проверки.");
        string result = command.Status == CaseCheckStatus.Planned ? Optional(command.Result, 4000) ?? ""
            : Required(command.Result, 3, 4000, "Укажите результат проверки.");
        if (check == null)
        {
            check = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, PropertyCaseId = row.Case.Id,
                AuthorEmployeeId = context.EmployeeId, RecordedAt = time.GetUtcNow() };
            db.CaseChecks.Add(check);
        }
        check.Level = template?.Level ?? command.Level; check.Title = title; check.Status = command.Status;
        if (check.Id == Guid.Empty || command.UpdateResponsible || check.ResponsibleEmployeeId == null && command.ResponsibleEmployeeId != null)
            check.ResponsibleEmployeeId = command.ResponsibleEmployeeId;
        if (template != null && check.TemplateItemId == null)
        {
            check.TemplateItemId = template.Id; check.TemplateItemVersion = template.Version; check.DescriptionSnapshot = template.Description;
        }
        else if (check.TemplateItemId == null) check.DescriptionSnapshot = Optional(check.DescriptionSnapshot, 4000) ?? "";
        check.DueAt = command.DueAt;
        check.Cost = command.Cost == null ? null : DataConventions.RoundRubles(command.Cost.Value);
        check.Result = result; check.Blocker = command.Blocker;
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId, Kind = "Check", Title = (command.Level == CaseCheckLevel.Quick ? "Базовая проверка: " : "Глубокая проверка: ") + title,
            Body = result + (command.Blocker ? "\nБлокирует дальнейшее решение." : ""), DueAt = command.DueAt, RecordedAt = time.GetUtcNow()
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "CaseCheckSaved", "PropertyCase", row.Case.Id,
            new { check.Id, check.Level, check.Status, check.Blocker }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveCheckTemplateAsync(Subject subject, SaveCheckTemplate command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Level) || command.SortOrder < 0) throw new ArgumentException("Некорректные параметры шаблона проверки.");
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await RequireTemplateManagementPermissionAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        CaseCheckTemplateItem? item = null;
        if (command.Id != null)
        {
            item = await db.CaseCheckTemplateItems.SingleOrDefaultAsync(
                value => value.Id == command.Id && value.OrganizationId == context.OrganizationId, cancellationToken)
                ?? throw new AccessDeniedException();
        }
        if (item != null && item.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException();
        item ??= new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId };
        if (command.Id == null) db.CaseCheckTemplateItems.Add(item);
        item.Title = Required(command.Title, 3, 512, "Укажите название шаблона проверки.");
        item.Level = command.Level; item.Description = Optional(command.Description, 4000) ?? "";
        item.SortOrder = command.SortOrder; item.Active = command.Active;
        OrganizationWorkspace.AddAudit(db, context, subject, "CaseCheckTemplateSaved", "CaseCheckTemplateItem", item.Id,
            new { item.Title, item.Level, item.SortOrder, item.Active }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveInspectionTemplateAsync(Subject subject, SaveInspectionTemplate command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.AnswerType) || command.SortOrder < 0) throw new ArgumentException("Некорректные параметры пункта осмотра.");
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await RequireTemplateManagementPermissionAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        InspectionTemplateItem? item = null;
        if (command.Id != null)
        {
            item = await db.InspectionTemplateItems.SingleOrDefaultAsync(
                value => value.Id == command.Id && value.OrganizationId == context.OrganizationId, cancellationToken)
                ?? throw new AccessDeniedException();
        }
        if (item != null && item.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException();
        item ??= new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId };
        if (command.Id == null) db.InspectionTemplateItems.Add(item);
        item.Key = Required(command.Key, 2, 128, "Укажите стабильный код пункта.");
        item.Title = Required(command.Title, 3, 512, "Укажите название пункта."); item.SortOrder = command.SortOrder;
        item.AnswerType = command.AnswerType; item.OptionsJson = JsonSerializer.Serialize(command.Options.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray());
        if (item.AnswerType == InspectionAnswerType.Choice && command.Options.Length == 0) throw new ArgumentException("Добавьте варианты выбора.");
        item.Unit = Optional(command.Unit, 64) ?? ""; item.NormalAnswer = Optional(command.NormalAnswer, 512) ?? "";
        item.AllowAttachments = command.AllowAttachments; item.Required = command.Required; item.Active = command.Active;
        OrganizationWorkspace.AddAudit(db, context, subject, "InspectionTemplateSaved", "InspectionTemplateItem", item.Id,
            new { item.Key, item.Title, item.SortOrder, item.AnswerType, item.Active }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<InspectionTaskPage> ReadMyInspectionsAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.InspectionRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await (from inspection in db.SiteInspections.AsNoTracking()
                          join propertyCase in db.PropertyCases.AsNoTracking() on inspection.PropertyCaseId equals propertyCase.Id
                          where inspection.OrganizationId == context.OrganizationId
                              && inspection.InspectorEmployeeId == context.EmployeeId
                          orderby inspection.RequestedAt descending
                          select new { Inspection = inspection, Case = propertyCase })
            .Take(500).ToArrayAsync(cancellationToken);

        Guid[] inspectionIds = rows.Select(item => item.Inspection.Id).ToArray();
        var counts = await db.SiteInspectionItems.AsNoTracking()
            .Where(item => inspectionIds.Contains(item.InspectionId))
            .GroupBy(item => item.InspectionId)
            .Select(group => new
            {
                Id = group.Key,
                Total = group.Count(),
                Done = group.Count(item => item.Status != InspectionItemStatus.Unanswered)
            }).ToArrayAsync(cancellationToken);
        Dictionary<Guid, (int Total, int Done)> countMap = counts.ToDictionary(
            item => item.Id, item => (item.Total, item.Done));

        Guid[] employeeIds = rows.SelectMany(item => new Guid?[]
            { item.Inspection.RequestedByEmployeeId, item.Inspection.InspectorEmployeeId })
            .Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToArray();
        Dictionary<Guid, string> names = await db.Employees.AsNoTracking()
            .Where(item => employeeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
        string zone = await db.Organizations.Where(item => item.Id == context.OrganizationId)
            .Select(item => item.BusinessTimeZone).SingleAsync(cancellationToken);

        InspectionTaskItem[] items = rows
            .OrderBy(item => item.Inspection.Status == InspectionStatus.Completed)
            .ThenBy(item => item.Inspection.DueAt == null)
            .ThenBy(item => item.Inspection.DueAt)
            .ThenByDescending(item => item.Inspection.RequestedAt)
            .Take(200)
            .Select(item =>
            {
                (int total, int done) = countMap.GetValueOrDefault(item.Inspection.Id);
                InspectionTaskState state = item.Inspection.Status == InspectionStatus.Completed
                    ? InspectionTaskState.Completed
                    : item.Inspection.StartedAt == null ? InspectionTaskState.Assigned : InspectionTaskState.InProgress;
                return new InspectionTaskItem(item.Case.Id, item.Case.BusinessNumber, item.Case.WorkingTitle,
                    item.Case.WorkingLocation, item.Case.CadastralNumber,
                    item.Inspection.RequestedByEmployeeId == null ? null
                        : names.GetValueOrDefault(item.Inspection.RequestedByEmployeeId.Value, "Сотрудник"),
                    item.Inspection.RequestedAt, item.Inspection.DueAt, state, done, total);
            }).ToArray();
        return new(items, zone);
    }

    public async Task<InspectionWorkspaceView> ReadInspectionAsync(Subject subject, Guid caseId,
        CancellationToken cancellationToken)
    {
        AccessContext context = await access.ResolveAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        SiteInspection? inspection = await db.SiteInspections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PropertyCaseId == caseId && item.OrganizationId == context.OrganizationId,
                cancellationToken);

        bool procurementReader = await AllowedAsync(subject, Permissions.QueueRead, cancellationToken)
            && await VisibleCases(db, context).AnyAsync(item => item.Case.Id == caseId, cancellationToken);
        bool assignedInspector = inspection != null && inspection.InspectorEmployeeId == context.EmployeeId
            && await AllowedAsync(subject, Permissions.InspectionRead, cancellationToken);
        if (!procurementReader && !assignedInspector) throw new AccessDeniedException();

        PropertyCase propertyCase = await db.PropertyCases.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == caseId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        SiteInspectionItem[] inspectionItems = inspection == null ? [] : await db.SiteInspectionItems.AsNoTracking()
            .Where(item => item.InspectionId == inspection.Id).OrderBy(item => item.SortOrderSnapshot)
            .ToArrayAsync(cancellationToken);
        var attachments = await (from link in db.CaseAttachments.AsNoTracking()
                                 join file in db.StoredFiles.AsNoTracking() on link.StoredFileId equals file.Id
                                 where link.PropertyCaseId == caseId
                                     && (link.OwnerType == CaseAttachmentOwner.Inspection
                                         || link.OwnerType == CaseAttachmentOwner.InspectionItem)
                                 orderby link.RecordedAt descending
                                 select new { Link = link, File = file }).ToArrayAsync(cancellationToken);

        string[] photoJson = await (from sourceLink in db.PropertyCaseSourceLinks.AsNoTracking()
                                    join listing in db.Listings.AsNoTracking() on sourceLink.CatalogItemId equals listing.Id
                                    where sourceLink.PropertyCaseId == caseId && sourceLink.Confirmed
                                    select listing.PhotosJson).ToArrayAsync(cancellationToken);
        string[] photos = photoJson.SelectMany(value => JsonSerializer.Deserialize<string[]>(value) ?? [])
            .Distinct(StringComparer.Ordinal).ToArray();

        Dictionary<Guid, string> names = await db.Employees.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId)
            .ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);

        bool canAssign = procurementReader && inspection?.Status != InspectionStatus.Completed
            && await AllowedAsync(subject, Permissions.InspectionRequest, cancellationToken);
        bool procurementPerformer = procurementReader
            && (await AllowedAsync(subject, Permissions.ManagerDecide, cancellationToken)
                || await AllowedAsync(subject, Permissions.HeadDecide, cancellationToken));
        bool inspectorPerformer = inspection != null && inspection.InspectorEmployeeId == context.EmployeeId
            && await AllowedAsync(subject, Permissions.InspectionPerform, cancellationToken);
        bool canPerform = inspection != null && inspection.Status != InspectionStatus.Completed
            && propertyCase.StageId is not ("rejected" or "monitor" or "acquired")
            && (procurementPerformer || inspectorPerformer);

        DecisionTarget[] inspectors = [];
        if (canAssign)
        {
            inspectors = await (from employee in db.Employees.AsNoTracking()
                                join assignment in db.EmployeeAssignments.AsNoTracking() on employee.Id equals assignment.EmployeeId
                                where employee.OrganizationId == context.OrganizationId && employee.Active
                                    && db.RolePermissions.Any(grant => grant.RoleId == assignment.RoleId
                                        && grant.PermissionId == Permissions.InspectionRead)
                                    && db.RolePermissions.Any(grant => grant.RoleId == assignment.RoleId
                                        && grant.PermissionId == Permissions.InspectionPerform)
                                select new { employee.Id, employee.DisplayName })
                .Distinct().OrderBy(item => item.DisplayName)
                .Select(item => new DecisionTarget(item.Id, item.DisplayName))
                .ToArrayAsync(cancellationToken);
        }

        InspectionView? inspectionView = inspection == null ? null : new InspectionView(
            inspection.Id, inspection.Status, inspection.OverallConclusion, inspection.PreliminaryDecision,
            names.GetValueOrDefault(inspection.InspectorEmployeeId, "Сотрудник"), inspection.StartedAt,
            inspection.CompletedAt, inspection.Version,
            inspectionItems.Select(item => new InspectionItemView(item.Id, item.TemplateItemId, item.TemplateItemVersion,
                item.TitleSnapshot, item.SortOrderSnapshot, item.AnswerTypeSnapshot,
                JsonSerializer.Deserialize<string[]>(item.OptionsJsonSnapshot) ?? [], item.UnitSnapshot,
                item.NormalAnswerSnapshot, item.AllowAttachmentsSnapshot, item.RequiredSnapshot, item.Status,
                item.Answer, item.Note, item.Version)).ToArray());

        InspectionAssignmentView? assignmentView = inspection == null ? null : new InspectionAssignmentView(
            inspection.InspectorEmployeeId, names.GetValueOrDefault(inspection.InspectorEmployeeId, "Сотрудник"),
            inspection.RequestedByEmployeeId == null ? null
                : names.GetValueOrDefault(inspection.RequestedByEmployeeId.Value, "Сотрудник"),
            inspection.RequestedAt, inspection.DueAt, inspection.Instructions);

        AttachmentView[] attachmentViews = attachments.Select(item => new AttachmentView(item.Link.Id,
            item.Link.OwnerType, AttachmentOwnerId(item.Link), item.Link.Kind, item.Link.Label, item.Link.Description,
            AttachmentOwnerLabel(item.Link, Array.Empty<CaseNegotiation>(), Array.Empty<CaseCheck>(), inspectionItems),
            item.File.OriginalName, item.File.ContentType, item.File.SizeBytes, item.File.Status,
            item.File.ExternalUrl != null, item.Link.RecordedAt, item.Link.DocumentRequirementId)).ToArray();

        return new(propertyCase.Id, propertyCase.BusinessNumber, propertyCase.WorkingTitle,
            propertyCase.WorkingLocation, propertyCase.CadastralNumber, photos, assignmentView, inspectionView,
            attachmentViews, inspectors, canAssign, canPerform, procurementReader);
    }

    public async Task AssignInspectionAsync(Subject subject, AssignInspection command, string correlationId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (command.DueAt is DateTimeOffset due && (due.Offset != TimeSpan.Zero || due > now.AddYears(2)))
            throw new ArgumentException("Укажите корректный срок осмотра в UTC.");
        string instructions = Optional(command.Instructions, 2000) ?? "";

        AccessContext context = await access.RequireAsync(subject, Permissions.InspectionRequest, cancellationToken);
        await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PropertyCases.FromSqlInterpolated(
            $"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId,
            cancellationToken) ?? throw new AccessDeniedException();
        if (row.Case.StageId is "rejected" or "monitor" or "acquired")
            throw new ArgumentException("Осмотр нельзя назначить в текущем состоянии PropertyCase.");

        string? inspectorName = await (from employee in db.Employees
                                      join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
                                      where employee.Id == command.InspectorEmployeeId
                                          && employee.OrganizationId == context.OrganizationId && employee.Active
                                          && db.RolePermissions.Any(grant => grant.RoleId == assignment.RoleId
                                              && grant.PermissionId == Permissions.InspectionRead)
                                          && db.RolePermissions.Any(grant => grant.RoleId == assignment.RoleId
                                              && grant.PermissionId == Permissions.InspectionPerform)
                                      select employee.DisplayName).SingleOrDefaultAsync(cancellationToken);
        if (inspectorName == null) throw new AccessDeniedException();

        SiteInspection? inspection = await db.SiteInspections.SingleOrDefaultAsync(
            item => item.PropertyCaseId == row.Case.Id, cancellationToken);
        if (command.DueAt is DateTimeOffset requestedDue && requestedDue < now
            && (inspection == null || inspection.DueAt != requestedDue))
            throw new ArgumentException("Новый или изменённый срок осмотра должен быть в будущем.");
        Guid? previousInspector = inspection?.InspectorEmployeeId;
        if (inspection == null)
        {
            if (command.ExpectedInspectionVersion != null) throw new DbUpdateConcurrencyException();
            InspectionTemplateItem[] templates = await EnsureInspectionTemplateAsync(db, context.OrganizationId, cancellationToken);
            inspection = new SiteInspection
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, PropertyCaseId = row.Case.Id,
                InspectorEmployeeId = command.InspectorEmployeeId, RequestedByEmployeeId = context.EmployeeId,
                RequestedAt = now, DueAt = command.DueAt, Instructions = instructions
            };
            db.SiteInspections.Add(inspection);
            db.SiteInspectionItems.AddRange(templates.Where(item => item.Active).Select(item => new SiteInspectionItem
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, InspectionId = inspection.Id,
                TemplateItemId = item.Id, TemplateItemVersion = item.Version, TitleSnapshot = item.Title,
                SortOrderSnapshot = item.SortOrder, AnswerTypeSnapshot = item.AnswerType,
                OptionsJsonSnapshot = item.OptionsJson, UnitSnapshot = item.Unit,
                NormalAnswerSnapshot = item.NormalAnswer, AllowAttachmentsSnapshot = item.AllowAttachments,
                RequiredSnapshot = item.Required
            }));
        }
        else
        {
            if (inspection.Version != command.ExpectedInspectionVersion) throw new DbUpdateConcurrencyException();
            if (inspection.Status == InspectionStatus.Completed)
                throw new ArgumentException("Завершённый осмотр нельзя переназначить.");
            inspection.InspectorEmployeeId = command.InspectorEmployeeId;
            inspection.RequestedByEmployeeId = context.EmployeeId;
            inspection.RequestedAt = now;
            inspection.DueAt = command.DueAt;
            inspection.Instructions = instructions;
        }

        db.BusinessTimeline.Add(new BusinessTimelineEntry
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
            ObjectId = row.Case.Id, ActorEmployeeId = context.EmployeeId, Kind = "InspectionAssigned",
            Title = previousInspector == null ? "Назначен осмотр участка" : "Осмотр переназначен",
            Body = instructions, TargetEmployeeId = command.InspectorEmployeeId, DueAt = command.DueAt, RecordedAt = now
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "SiteInspectionAssigned", "PropertyCase", row.Case.Id,
            new { InspectionId = inspection.Id, PreviousInspector = previousInspector, command.InspectorEmployeeId,
                Inspector = inspectorName, command.DueAt, Instructions = instructions }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid> SaveInspectionAsync(Subject subject, SaveInspection command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.ResolveAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        SiteInspection? inspection = await db.SiteInspections.SingleOrDefaultAsync(
            item => item.PropertyCaseId == command.CaseId && item.OrganizationId == context.OrganizationId,
            cancellationToken);
        bool dossierPerformer = await AllowedAsync(subject, Permissions.ManagerDecide, cancellationToken)
            || await AllowedAsync(subject, Permissions.HeadDecide, cancellationToken);
        bool assignedInspector = inspection != null && inspection.InspectorEmployeeId == context.EmployeeId
            && await AllowedAsync(subject, Permissions.InspectionPerform, cancellationToken);

        Row row;
        if (dossierPerformer)
        {
            await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
            row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId,
                cancellationToken) ?? throw new AccessDeniedException();
        }
        else if (assignedInspector)
        {
            PropertyCase propertyCase = await db.PropertyCases.SingleOrDefaultAsync(
                item => item.Id == command.CaseId && item.OrganizationId == context.OrganizationId, cancellationToken)
                ?? throw new AccessDeniedException();
            row = new Row { Case = propertyCase };
        }
        else
        {
            throw new AccessDeniedException();
        }

        if (row.Case.StageId is "rejected" or "monitor" or "acquired")
            throw new ArgumentException("Осмотр нельзя изменять в текущем состоянии PropertyCase.");

        if (inspection == null)
        {
            if (!dossierPerformer || command.InspectionId != null) throw new AccessDeniedException();
            InspectionTemplateItem[] templates = await EnsureInspectionTemplateAsync(db, context.OrganizationId, cancellationToken);
            DateTimeOffset now = time.GetUtcNow();
            inspection = new SiteInspection
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, PropertyCaseId = row.Case.Id,
                InspectorEmployeeId = context.EmployeeId, RequestedByEmployeeId = context.EmployeeId,
                RequestedAt = now, StartedAt = now
            };
            db.SiteInspections.Add(inspection);
            db.SiteInspectionItems.AddRange(templates.Where(item => item.Active).Select(item => new SiteInspectionItem
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, InspectionId = inspection.Id,
                TemplateItemId = item.Id, TemplateItemVersion = item.Version, TitleSnapshot = item.Title,
                SortOrderSnapshot = item.SortOrder, AnswerTypeSnapshot = item.AnswerType, OptionsJsonSnapshot = item.OptionsJson,
                UnitSnapshot = item.Unit, NormalAnswerSnapshot = item.NormalAnswer, AllowAttachmentsSnapshot = item.AllowAttachments,
                RequiredSnapshot = item.Required
            }));
            OrganizationWorkspace.AddAudit(db, context, subject, "SiteInspectionStarted", "PropertyCase", row.Case.Id,
                new { InspectionId = inspection.Id, inspection.InspectorEmployeeId }, correlationId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return inspection.Id;
        }

        if (command.InspectionId != inspection.Id || command.ExpectedInspectionVersion != inspection.Version)
            throw new DbUpdateConcurrencyException();
        if (inspection.Status == InspectionStatus.Completed)
            throw new ArgumentException("Завершённый осмотр доступен только для чтения.");

        bool startedNow = false;
        if (inspection.StartedAt == null)
        {
            inspection.StartedAt = time.GetUtcNow();
            startedNow = true;
            OrganizationWorkspace.AddAudit(db, context, subject, "SiteInspectionStarted", "PropertyCase", row.Case.Id,
                new { InspectionId = inspection.Id, inspection.InspectorEmployeeId }, correlationId);
        }

        SiteInspectionItem[] items = await db.SiteInspectionItems.Where(item => item.InspectionId == inspection.Id)
            .ToArrayAsync(cancellationToken);
        foreach (InspectionAnswer answer in command.Answers)
        {
            SiteInspectionItem item = items.SingleOrDefault(value => value.Id == answer.ItemId)
                ?? throw new AccessDeniedException();
            if (item.Version != answer.ExpectedItemVersion) throw new DbUpdateConcurrencyException();
            if (!Enum.IsDefined(answer.Status)) throw new ArgumentException("Некорректное состояние пункта осмотра.");
            string value = Optional(answer.Answer, 4000) ?? "";
            string note = Optional(answer.Note, 4000) ?? "";
            value = NormalizeInspectionAnswer(item, answer.Status, value);
            item.Status = answer.Status;
            item.Answer = value;
            item.Note = note;
        }

        inspection.OverallConclusion = Optional(command.OverallConclusion, 4000) ?? "";
        inspection.PreliminaryDecision = Optional(command.PreliminaryDecision, 1000) ?? "";
        if (command.Complete)
        {
            if (items.Any(item => item.RequiredSnapshot && item.Status == InspectionItemStatus.Unanswered))
                throw new ArgumentException("Обработайте обязательные пункты осмотра.");
            inspection.OverallConclusion = Required(command.OverallConclusion, 3, 4000, "Укажите общий вывод осмотра.");
            inspection.Status = InspectionStatus.Completed;
            inspection.CompletedAt = time.GetUtcNow();
            db.BusinessTimeline.Add(new BusinessTimelineEntry
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
                ObjectId = row.Case.Id, ActorEmployeeId = context.EmployeeId, Kind = "Inspection",
                Title = "Осмотр участка завершён", Body = inspection.OverallConclusion,
                RecordedAt = inspection.CompletedAt.Value
            });
        }

        if (!startedNow || command.Answers.Count > 0 || command.Complete
            || inspection.OverallConclusion.Length > 0 || inspection.PreliminaryDecision.Length > 0)
        {
            OrganizationWorkspace.AddAudit(db, context, subject,
                command.Complete ? "SiteInspectionCompleted" : "SiteInspectionDraftSaved",
                "PropertyCase", row.Case.Id, new { InspectionId = inspection.Id, inspection.Status }, correlationId);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return inspection.Id;
    }

    public async Task MarkAcquiredAsync(Subject subject, MarkCaseAcquired command, string correlationId, CancellationToken cancellationToken)
    {
        if (command.ActualPrice <= 0) throw new ArgumentException("Фактическая цена покупки должна быть положительной.");
        if (command.AcquisitionDate > DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime.AddDays(1))) throw new ArgumentException("Дата покупки не может быть в будущем.");
        AccessContext context = await access.RequireAsync(subject, Permissions.PurchaseConfirm, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        decimal price = DataConventions.RoundRubles(command.ActualPrice); string comment = Optional(command.Comment, 4000) ?? "";
        if (row.Case.StageId == "acquired")
        {
            if (row.Case.AcquisitionPrice == price && row.Case.AcquisitionDate == command.AcquisitionDate && row.Case.AcquisitionComment == comment) return;
            throw new ArgumentException("PropertyCase уже отмечен как купленный.");
        }
        if (row.Case.Version != command.ExpectedCaseVersion) throw new DbUpdateConcurrencyException();
        if (row.Case.StageId is "rejected" or "monitor") throw new ArgumentException("Сначала возобновите PropertyCase.");
        string from = row.Case.StageId; DateTimeOffset now = time.GetUtcNow();
        row.Case.StageId = "acquired"; row.Case.AcquisitionPrice = price; row.Case.AcquisitionDate = command.AcquisitionDate;
        row.Case.AcquisitionComment = comment; row.Case.AcquiredByEmployeeId = context.EmployeeId; row.Case.AcquiredAt = now;
        db.Entry(row.Case).Property(item => item.Version).IsModified = true; row.Task.Completed = true;
        db.WorkflowTransitions.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = row.Case.Id,
            FromStageId = from, ToStageId = "acquired", Action = "Acquire", ActorEmployeeId = context.EmployeeId,
            ObjectVersion = row.Case.Version + 1, RecordedAt = now
        });
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId, Kind = "Acquisition", Title = $"Объект куплен за {price:N0} ₽",
            Body = $"Дата покупки / регистрации: {command.AcquisitionDate:dd.MM.yyyy}" + (comment.Length == 0 ? "" : "\n" + comment),
            EffectiveAt = new DateTimeOffset(command.AcquisitionDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), RecordedAt = now
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "PropertyCaseAcquired", "PropertyCase", row.Case.Id,
            new { ActualPrice = price, command.AcquisitionDate, Comment = comment, From = from, To = "acquired" }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task CorrectAcquisitionAsync(Subject subject, CorrectCaseAcquisition command, string correlationId, CancellationToken cancellationToken)
    {
        if (command.ActualPrice <= 0) throw new ArgumentException("Фактическая цена покупки должна быть положительной.");
        if (command.AcquisitionDate > DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime.AddDays(1))) throw new ArgumentException("Дата покупки не может быть в будущем.");
        string reason = Required(command.Reason, 3, 1000, "Укажите причину исправления от 3 до 1000 символов.");
        AccessContext context = await access.RequireAsync(subject, Permissions.PurchaseConfirm, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        if (row.Case.StageId != "acquired") throw new ArgumentException("Исправить можно только уже подтверждённую покупку.");
        if (row.Case.Version != command.ExpectedCaseVersion) throw new DbUpdateConcurrencyException();
        decimal price = DataConventions.RoundRubles(command.ActualPrice);
        string comment = Optional(command.Comment, 4000) ?? "";
        var previous = new { row.Case.AcquisitionPrice, row.Case.AcquisitionDate, row.Case.AcquisitionComment };
        if (previous.AcquisitionPrice == price && previous.AcquisitionDate == command.AcquisitionDate && previous.AcquisitionComment == comment)
            throw new ArgumentException("Новые данные совпадают с текущими.");
        DateTimeOffset now = time.GetUtcNow();
        row.Case.AcquisitionPrice = price;
        row.Case.AcquisitionDate = command.AcquisitionDate;
        row.Case.AcquisitionComment = comment;
        db.Entry(row.Case).Property(item => item.Version).IsModified = true;
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId, Kind = "AcquisitionCorrection", Title = "Исправлены данные покупки",
            Body = $"Причина: {reason}\nЦена: {previous.AcquisitionPrice:N0} ₽ → {price:N0} ₽\nДата: {previous.AcquisitionDate:dd.MM.yyyy} → {command.AcquisitionDate:dd.MM.yyyy}" +
                (comment.Length == 0 ? "" : "\n" + comment),
            EffectiveAt = new DateTimeOffset(command.AcquisitionDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), RecordedAt = now
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "PropertyCaseAcquisitionCorrected", "PropertyCase", row.Case.Id,
            new { Previous = previous, Current = new { ActualPrice = price, command.AcquisitionDate, Comment = comment }, Reason = reason }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid> AddAttachmentAsync(Subject subject, AddCaseAttachment command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.OwnerType) || !Enum.IsDefined(command.Kind)) throw new ArgumentException("Некорректный тип вложения.");
        AccessContext context = await access.ResolveAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        bool inspectionOwner = command.OwnerType is CaseAttachmentOwner.Inspection or CaseAttachmentOwner.InspectionItem
            && command.DocumentRequirementId == null;
        bool assignedInspector = inspectionOwner && await AllowedAsync(subject, Permissions.InspectionPerform, cancellationToken)
            && await db.SiteInspections.AnyAsync(item => item.PropertyCaseId == command.CaseId
                && item.OrganizationId == context.OrganizationId && item.InspectorEmployeeId == context.EmployeeId
                && item.Status == InspectionStatus.Draft, cancellationToken);
        Row row;
        if (assignedInspector)
        {
            PropertyCase propertyCase = await db.PropertyCases.SingleOrDefaultAsync(
                item => item.Id == command.CaseId && item.OrganizationId == context.OrganizationId, cancellationToken)
                ?? throw new AccessDeniedException();
            if (propertyCase.StageId is "rejected" or "monitor" or "acquired")
                throw new ArgumentException("Материалы нельзя добавлять к осмотру в текущем состоянии PropertyCase.");
            row = new Row { Case = propertyCase };
        }
        else
        {
            context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
            await RequireDossierPermissionAsync(subject, cancellationToken);
            row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken)
                ?? throw new AccessDeniedException();
        }
        Guid? negotiationId = command.OwnerType == CaseAttachmentOwner.Negotiation ? command.OwnerId : null;
        Guid? checkId = command.OwnerType == CaseAttachmentOwner.Check ? command.OwnerId : null;
        Guid? inspectionId = command.OwnerType == CaseAttachmentOwner.Inspection ? command.OwnerId : null;
        Guid? inspectionItemId = command.OwnerType == CaseAttachmentOwner.InspectionItem ? command.OwnerId : null;
        CaseDocumentRequirement? documentRequirement = command.DocumentRequirementId == null ? null
            : await db.CaseDocumentRequirements.SingleOrDefaultAsync(item => item.Id == command.DocumentRequirementId
                && item.PropertyCaseId == row.Case.Id, cancellationToken) ?? throw new AccessDeniedException();
        if (documentRequirement != null && command.Kind != CaseAttachmentKind.Document)
            throw new ArgumentException("С пунктом чек-листа можно связать только документ.");
        if (command.OwnerType == CaseAttachmentOwner.Case && command.OwnerId != null
            || command.OwnerType == CaseAttachmentOwner.Negotiation && (negotiationId == null || !await db.CaseNegotiations.AnyAsync(item => item.Id == negotiationId && item.PropertyCaseId == row.Case.Id, cancellationToken))
            || command.OwnerType == CaseAttachmentOwner.Check && (checkId == null || !await db.CaseChecks.AnyAsync(item => item.Id == checkId && item.PropertyCaseId == row.Case.Id, cancellationToken))
            || command.OwnerType == CaseAttachmentOwner.Inspection && (inspectionId == null || !await db.SiteInspections.AnyAsync(item => item.Id == inspectionId && item.PropertyCaseId == row.Case.Id, cancellationToken))
            || command.OwnerType == CaseAttachmentOwner.InspectionItem && (inspectionItemId == null || !await db.SiteInspectionItems.AnyAsync(item => item.Id == inspectionItemId
                && db.SiteInspections.Any(inspection => inspection.Id == item.InspectionId && inspection.PropertyCaseId == row.Case.Id), cancellationToken)))
            throw new AccessDeniedException();
        string? externalUrl = Optional(command.ExternalUrl, 2000);
        bool external = externalUrl != null;
        if (external && (!Uri.TryCreate(externalUrl, UriKind.Absolute, out Uri? parsed) || parsed.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Внешняя ссылка должна использовать HTTPS.");
        if (external != (command.Kind == CaseAttachmentKind.Link) || external == (command.Content != null))
            throw new ArgumentException("Передайте либо HTTPS-ссылку, либо содержимое файла подходящего типа.");
        if (!external) FileUploadLimits.EnsureRawFileSize(command.Content!.LongLength);
        string contentType = external ? "text/uri-list" : Required(command.ContentType, 3, 256, "Укажите MIME-тип файла.");
        if (!external && !AllowedContentType(command.Kind, contentType)) throw new ArgumentException("Тип файла не разрешён для выбранного вложения.");
        Guid storedId = DataConventions.NewId(); Guid attachmentId = DataConventions.NewId(); DateTimeOffset now = time.GetUtcNow();
        StoredFile stored = new()
        {
            Id = storedId, OrganizationId = context.OrganizationId, OwnerModule = "Procurement", Purpose = command.Kind.ToString(),
            ExternalUrl = externalUrl, OriginalName = external ? Required(command.Label, 2, 512, "Укажите название ссылки.") : Required(command.OriginalName, 1, 512, "Укажите имя файла."),
            ContentType = contentType, Status = external ? StoredFileStatus.Available : StoredFileStatus.PendingUpload,
            CreatedByEmployeeId = context.EmployeeId, RecordedAt = now
        };
        db.StoredFiles.Add(stored);
        db.CaseAttachments.Add(new()
        {
            Id = attachmentId, OrganizationId = context.OrganizationId, PropertyCaseId = row.Case.Id, StoredFileId = storedId,
            OwnerType = command.OwnerType, NegotiationId = negotiationId, CheckId = checkId, InspectionId = inspectionId,
            InspectionItemId = inspectionItemId, DocumentRequirementId = documentRequirement?.Id, Kind = command.Kind,
            Label = Required(command.Label, 2, 512, "Укажите понятное название вложения."),
            Description = Optional(command.Description, 4000) ?? "", ActorEmployeeId = context.EmployeeId, RecordedAt = now
        });
        // Files need a durable recovery record before provider I/O. External links have no
        // provider step and can be committed atomically with their business metadata below.
        if (!external) await db.SaveChangesAsync(cancellationToken);
        if (!external)
        {
            try
            {
                FileWriteResult write = await fileStorage.WriteAsync(new FileWriteRequest(storedId, context.OrganizationId,
                    row.Case.Id, command.Kind.ToString(), contentType), command.Content!, cancellationToken);
                stored.StorageKey = write.StorageKey; stored.Sha256 = write.Sha256; stored.SizeBytes = write.SizeBytes;
                stored.Status = StoredFileStatus.Available;
                // Do not persist Available separately. If the final DB commit is interrupted,
                // PendingUpload remains authoritative and retry continues this same StoredFile.
            }
            catch
            {
                stored.Status = StoredFileStatus.UploadFailed;
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }
        if (documentRequirement != null)
        {
            documentRequirement.Status = CaseDocumentStatus.Received;
            documentRequirement.Note = Optional(command.Description, 2000) ?? documentRequirement.Note;
            documentRequirement.UpdatedByEmployeeId = context.EmployeeId;
            documentRequirement.UpdatedAt = now;
            db.Entry(row.Case).Property(item => item.Version).IsModified = true;
        }
        OrganizationWorkspace.AddAudit(db, context, subject, "CaseAttachmentAdded", "PropertyCase", row.Case.Id,
            new { AttachmentId = attachmentId, command.OwnerType, command.Kind, command.DocumentRequirementId, External = external }, correlationId);
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId, Kind = "Attachment", Title = "Добавлено вложение", Body = command.Label, RecordedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return attachmentId;
    }

    public async Task SaveDocumentRequirementAsync(Subject subject, SaveDocumentRequirement command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Status)) throw new ArgumentException("Некорректный статус документа.");
        if (command.DueAt?.Offset != null && command.DueAt.Value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Срок документа должен быть указан в UTC.");
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await RequireDossierPermissionAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        CaseDocumentRequirement requirement = await db.CaseDocumentRequirements.SingleOrDefaultAsync(item => item.Id == command.RequirementId
            && item.PropertyCaseId == row.Case.Id, cancellationToken) ?? throw new AccessDeniedException();
        if (row.Case.Version != command.ExpectedCaseVersion || requirement.Version != command.ExpectedRequirementVersion)
            throw new DbUpdateConcurrencyException();
        bool hasDocument = await (from attachment in db.CaseAttachments
                                  join file in db.StoredFiles on attachment.StoredFileId equals file.Id
                                  where attachment.DocumentRequirementId == requirement.Id && file.Status == StoredFileStatus.Available
                                  select attachment.Id).AnyAsync(cancellationToken);
        if (command.Status is CaseDocumentStatus.Received or CaseDocumentStatus.Verified && !hasDocument)
            throw new ArgumentException("Сначала прикрепите документ к этому пункту чек-листа.");
        string note = Optional(command.Note, 2000) ?? "";
        DateTimeOffset now = time.GetUtcNow();
        requirement.Status = command.Status;
        requirement.DueAt = command.DueAt;
        requirement.Note = note;
        requirement.UpdatedByEmployeeId = context.EmployeeId;
        requirement.UpdatedAt = now;
        db.Entry(row.Case).Property(item => item.Version).IsModified = true;
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId, Kind = "Document", Title = $"Документ «{requirement.Title}»: {DocumentStatusLabel(command.Status)}",
            Body = note, DueAt = command.DueAt, RecordedAt = now
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "CaseDocumentRequirementChanged", "PropertyCase", row.Case.Id,
            new { requirement.Id, requirement.Code, command.Status, command.DueAt }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AttachmentContent> ReadAttachmentAsync(Subject subject, Guid attachmentId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.ResolveAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var value = await (from link in db.CaseAttachments.AsNoTracking()
                           join file in db.StoredFiles.AsNoTracking() on link.StoredFileId equals file.Id
                           where link.Id == attachmentId && link.OrganizationId == context.OrganizationId
                           select new { Link = link, File = file }).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
        bool procurementReader = await AllowedAsync(subject, Permissions.QueueRead, cancellationToken)
            && await VisibleCases(db, context).AnyAsync(item => item.Case.Id == value.Link.PropertyCaseId, cancellationToken);
        bool inspectorReader = value.Link.OwnerType is CaseAttachmentOwner.Inspection or CaseAttachmentOwner.InspectionItem
            && await AllowedAsync(subject, Permissions.InspectionRead, cancellationToken)
            && await db.SiteInspections.AnyAsync(item => item.PropertyCaseId == value.Link.PropertyCaseId
                && item.OrganizationId == context.OrganizationId && item.InspectorEmployeeId == context.EmployeeId,
                cancellationToken);
        if (!procurementReader && !inspectorReader) throw new AccessDeniedException();
        if (value.File.Status != StoredFileStatus.Available) throw new InvalidOperationException("Вложение ещё не доступно.");
        if (value.File.ExternalUrl != null) return new(value.File.OriginalName, value.File.ContentType, null, value.File.ExternalUrl);
        if (value.File.StorageKey == null) throw new InvalidOperationException("Вложение повреждено.");
        byte[] content = await fileStorage.ReadAsync(value.File.StorageKey, cancellationToken);
        // Authorization above must happen before any provider request. Metadata remains the
        // authority even if an operator changes the underlying file outside LandErp.
        if (content.LongLength != value.File.SizeBytes || !string.Equals(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)), value.File.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new FileStorageException("STORAGE_INTEGRITY", false, "Проверка целостности вложения не пройдена.");
        return new(value.File.OriginalName, value.File.ContentType, content, null);
    }

    public async Task RetryAttachmentAsync(Subject subject, RetryCaseAttachment command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await RequireDossierPermissionAsync(subject, cancellationToken);
        FileUploadLimits.EnsureRawFileSize(command.Content.LongLength);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var value = await (from link in db.CaseAttachments join file in db.StoredFiles on link.StoredFileId equals file.Id
                           where link.Id == command.AttachmentId && link.PropertyCaseId == command.CaseId && link.OrganizationId == context.OrganizationId
                           select new { Link = link, File = file }).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
        PropertyCase propertyCase = await VisibleCases(db, context).Where(item => item.Case.Id == command.CaseId)
            .Select(item => item.Case).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
        if (value.File.Status is not (StoredFileStatus.UploadFailed or StoredFileStatus.PendingUpload)) throw new ArgumentException("Повторная загрузка этому вложению не требуется.");
        string contentType = Required(command.ContentType, 3, 256, "Укажите MIME-тип файла.");
        if (!AllowedContentType(value.Link.Kind, contentType)) throw new ArgumentException("Тип файла не разрешён для выбранного вложения.");
        FileWriteResult write;
        try
        {
            write = await fileStorage.WriteAsync(new FileWriteRequest(value.File.Id, context.OrganizationId,
                command.CaseId, value.Link.Kind.ToString(), contentType), command.Content, cancellationToken);
        }
        catch
        {
            value.File.Status = StoredFileStatus.UploadFailed;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        // Commit recovery as one DB state transition. A DB failure after provider success leaves
        // the previous PendingUpload/UploadFailed state recoverable with the same StoredFile.Id.
        value.File.OriginalName = Required(command.OriginalName, 1, 512, "Укажите имя файла.");
        value.File.ContentType = contentType;
        value.File.StorageKey = write.StorageKey;
        value.File.Sha256 = write.Sha256;
        value.File.SizeBytes = write.SizeBytes;
        value.File.Status = StoredFileStatus.Available;
        if (value.Link.DocumentRequirementId is Guid requirementId)
        {
            CaseDocumentRequirement requirement = await db.CaseDocumentRequirements.SingleAsync(item => item.Id == requirementId, cancellationToken);
            requirement.Status = CaseDocumentStatus.Received;
            requirement.UpdatedByEmployeeId = context.EmployeeId;
            requirement.UpdatedAt = time.GetUtcNow();
            db.Entry(propertyCase).Property(item => item.Version).IsModified = true;
        }
        OrganizationWorkspace.AddAudit(db, context, subject, "CaseAttachmentUploadRetried", "PropertyCase", command.CaseId,
            new { command.AttachmentId, value.Link.OwnerType }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplySourceFactAsync(Subject subject, ApplySourceFact command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Field)) throw new ArgumentException("Поле не поддерживается.");
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await RequireDossierPermissionAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        if (row.Case.Version != command.ExpectedCaseVersion) throw new DbUpdateConcurrencyException();
        Listing source = await (from link in db.PropertyCaseSourceLinks
                                join item in db.Listings on link.CatalogItemId equals item.Id
                                where link.PropertyCaseId == row.Case.Id && link.CatalogItemId == command.CatalogItemId && link.Confirmed
                                select item).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
        string value = ApplyFact(row.Case, source, command.Field);
        row.Case.FactsProvenance = $"Подтверждено сотрудником из {source.Source} {time.GetUtcNow():O}";
        db.Entry(row.Case).Property(item => item.Version).IsModified = true;
        db.PropertyCaseFactRevisions.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, PropertyCaseId = row.Case.Id,
            CatalogItemId = source.Id, Field = command.Field, Value = value, VerifiedByEmployeeId = context.EmployeeId, RecordedAt = time.GetUtcNow()
        });
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId, Kind = "FactVerified", Title = "Рабочий факт подтверждён из источника",
            Body = $"{command.Field}: {value} ({source.Source})", RecordedAt = time.GetUtcNow()
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "CaseFactAppliedFromSource", "PropertyCase", row.Case.Id,
            new { source.Id, command.Field, Value = value }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CorrectCaseFactAsync(Subject subject, CorrectPropertyCaseFact command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Field)) throw new ArgumentException("Поле не поддерживается.");
        string reason = Required(command.Reason, 3, 4000, "Укажите причину исправления.");
        (string? textValue, decimal? numericValue) = NormalizeCorrection(command);

        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await RequireDossierPermissionAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PropertyCases.FromSqlInterpolated(
            $"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (row.Case.Version != command.ExpectedCaseVersion) throw new DbUpdateConcurrencyException();

        string before = CaseFactValue(row.Case, command.Field);
        ApplyCorrection(row.Case, command.Field, textValue, numericValue);
        string after = CaseFactValue(row.Case, command.Field);
        if (string.Equals(before, after, StringComparison.Ordinal))
            throw new ArgumentException("Новое значение не отличается от текущего.");

        DateTimeOffset now = time.GetUtcNow();
        row.Case.FactsProvenance = $"Исправлено сотрудником {now:O}";
        db.Entry(row.Case).Property(item => item.Version).IsModified = true;
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId, Kind = "FactCorrected", Title = "Рабочий факт исправлен",
            Body = $"{CaseFactLabel(command.Field)}: {TimelineFactValue(before)} → {TimelineFactValue(after)}\nПричина: {reason}", RecordedAt = now
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "PropertyCaseFactCorrected", "PropertyCase", row.Case.Id,
            new { Field = command.Field.ToString(), Before = before, After = after, Reason = reason }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveNextActionAsync(Subject subject, SaveNextAction command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Type)) throw new ArgumentException("Выберите поддерживаемый тип следующего действия.");
        string title = Required(command.Title, 3, 512, "Укажите название следующего действия от 3 до 512 символов.");
        string description = Required(command.Description, 3, 4000, "Укажите цель следующего действия от 3 до 4000 символов.");
        DateTimeOffset now = time.GetUtcNow();
        if (command.DueAt is DateTimeOffset due && (due.Offset != TimeSpan.Zero || due < now || due > now.AddYears(2)))
            throw new ArgumentException("Укажите будущий срок в UTC.");

        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await RequireDossierPermissionAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await EnsureActiveEmployeeAsync(db, context.EmployeeId, cancellationToken);
        await db.PropertyCases.FromSqlInterpolated(
            $"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (row.Case.Version != command.ExpectedCaseVersion || row.Task.Version != command.ExpectedTaskVersion)
            throw new DbUpdateConcurrencyException();
        if (row.Case.StageId == "acquired")
            throw new ArgumentException("Закупка уже завершена. Следующее действие для купленного объекта изменить нельзя.");

        DecisionTarget[] assignees = await DossierTargetsAsync(db, row.Case, row.Assignment.EmployeeId,
            ProcurementRecipientAccess.CurrentVisibility, cancellationToken);
        if (!assignees.Any(item => item.EmployeeId == command.AssigneeEmployeeId))
            throw new AccessDeniedException();

        var before = new { row.Task.Type, row.Task.Title, row.Task.Description, row.Task.DueAt, row.Task.EmployeeId };
        row.Task.Type = command.Type;
        row.Task.Title = title;
        row.Task.Description = description;
        row.Task.DueAt = command.DueAt;
        row.Task.EmployeeId = command.AssigneeEmployeeId;
        row.Task.Completed = false;
        db.Entry(row.Case).Property(item => item.Version).IsModified = true;

        string assigneeName = assignees.First(item => item.EmployeeId == command.AssigneeEmployeeId).Name;
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId, Kind = "NextActionChanged", Title = "Следующее действие обновлено",
            Body = $"{NextActionTypeLabel(command.Type)}: {title}\nЦель: {description}", TargetEmployeeId = command.AssigneeEmployeeId,
            DueAt = command.DueAt, RecordedAt = now
        });
        if (command.AssigneeEmployeeId != context.EmployeeId && command.AssigneeEmployeeId != before.EmployeeId)
        {
            db.Notifications.Add(new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, EmployeeId = command.AssigneeEmployeeId,
                ObjectType = "PropertyCase", ObjectId = row.Case.Id,
                Title = $"{row.Case.BusinessNumber}: назначено действие «{title}»", RecordedAt = now
            });
        }
        OrganizationWorkspace.AddAudit(db, context, subject, "ProcurementNextActionChanged", "PropertyCase", row.Case.Id,
            new { Before = before, After = new { command.Type, Title = title, Description = description, command.DueAt, command.AssigneeEmployeeId, Assignee = assigneeName } }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationView>> ReadNotificationsAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Notifications.Where(item => item.OrganizationId == context.OrganizationId && item.EmployeeId == context.EmployeeId)
            .OrderByDescending(item => item.RecordedAt).Take(50).Select(item => new NotificationView(item.Id, item.ObjectId, item.Title, item.RecordedAt, item.ReadAt != null))
            .ToArrayAsync(cancellationToken);
    }

    public async Task MarkNotificationReadAsync(Subject subject, Guid id, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        InternalNotification item = await db.Notifications.SingleOrDefaultAsync(value => value.Id == id && value.OrganizationId == context.OrganizationId && value.EmployeeId == context.EmployeeId, cancellationToken) ?? throw new AccessDeniedException();
        item.ReadAt ??= time.GetUtcNow(); await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureActiveEmployeeAsync(LandErpDbContext db, Guid employeeId,
        CancellationToken cancellationToken)
    {
        if (!await db.Employees.AnyAsync(item => item.Id == employeeId && item.Active, cancellationToken))
            throw new AccessDeniedException();
    }

    private async Task<bool> AllowedAsync(Subject subject, string permission, CancellationToken cancellationToken)
    { try { await access.RequireAsync(subject, permission, cancellationToken); return true; } catch (AccessDeniedException) { return false; } }

    private async Task RequireDossierPermissionAsync(Subject subject, CancellationToken cancellationToken)
    {
        if (!await AllowedAsync(subject, Permissions.ManagerDecide, cancellationToken)
            && !await AllowedAsync(subject, Permissions.HeadDecide, cancellationToken)) throw new AccessDeniedException();
    }

    private async Task RequireTemplateManagementPermissionAsync(Subject subject, CancellationToken cancellationToken)
    {
        // LR-23 remains a product decision. Preserve current Manager/Head rights behind an explicit seam.
        if (!await AllowedAsync(subject, Permissions.ManagerDecide, cancellationToken)
            && !await AllowedAsync(subject, Permissions.HeadDecide, cancellationToken)) throw new AccessDeniedException();
    }

    private static async Task<InspectionTemplateItem[]> EnsureInspectionTemplateAsync(LandErpDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        InspectionTemplateItem[] existing = await db.InspectionTemplateItems.Where(item => item.OrganizationId == organizationId)
            .OrderBy(item => item.SortOrder).ToArrayAsync(cancellationToken);
        if (existing.Length > 0) return existing;
        (string Key, string Title, InspectionAnswerType Type, string Unit, string Normal, string[] Options)[] defaults =
        [
            ("road-settlement", "Дорога до ближайшего населённого пункта", InspectionAnswerType.Choice, "", "Асфальт", ["Асфальт", "Щебень", "Грунт", "Плохая / сезонная"]),
            ("road-plot", "Дорога до участка", InspectionAnswerType.Number, "м", "", []),
            ("terrain", "Рельеф участка", InspectionAnswerType.Choice, "", "Ровный", ["Ровный", "Небольшой уклон", "Сильный уклон", "Низина"]),
            ("near-center", "Ближайший центр / население", InspectionAnswerType.Text, "", "", []),
            ("big-city", "Расстояние до крупного города", InspectionAnswerType.Number, "км", "", []),
            ("neighbors", "Наличие соседей", InspectionAnswerType.Boolean, "", "true", []),
            ("village-level", "Уровень деревни", InspectionAnswerType.Choice, "", "Высокий", ["Высокий", "Средний", "Низкий", "Нужно уточнить"]),
            ("shops", "Наличие магазинов / ТЦ", InspectionAnswerType.Boolean, "", "true", []),
            ("water", "Наличие водоёмов", InspectionAnswerType.Boolean, "", "true", []),
            ("lep", "Расстояние до ЛЭП", InspectionAnswerType.Number, "м", "", []),
            ("gas", "Магистральный газ", InspectionAnswerType.Boolean, "", "true", []),
            ("overgrowth", "Зарастание участка", InspectionAnswerType.Percentage, "%", "", []),
            ("forest", "Лес на участке", InspectionAnswerType.Percentage, "%", "", []),
            ("swamp", "На участке болото", InspectionAnswerType.Boolean, "", "false", []),
            ("through-plots", "Проезд через другие участки", InspectionAnswerType.Boolean, "", "false", []),
            ("dead-village", "Мёртвая деревня / нет соседей", InspectionAnswerType.Boolean, "", "false", []),
            ("bad-objects", "Свалка / полигон / МСЗ / военная база", InspectionAnswerType.Boolean, "", "false", []),
            ("animal-burial", "Скотомогильник", InspectionAnswerType.Boolean, "", "false", []),
            ("cemetery", "Кладбище", InspectionAnswerType.Boolean, "", "false", []),
            ("industrial-1km", "Промышленные объекты / свалки до 1 км", InspectionAnswerType.Boolean, "", "false", [])
        ];
        InspectionTemplateItem[] created = defaults.Select((value, index) => new InspectionTemplateItem
        {
            Id = DataConventions.NewId(), OrganizationId = organizationId, Key = value.Key, Title = value.Title,
            SortOrder = (index + 1) * 10, AnswerType = value.Type, Unit = value.Unit, NormalAnswer = value.Normal,
            OptionsJson = JsonSerializer.Serialize(value.Options), AllowAttachments = true, Required = true, Active = true
        }).ToArray();
        db.InspectionTemplateItems.AddRange(created); await db.SaveChangesAsync(cancellationToken); return created;
    }

    private static async Task<CaseCheckTemplateItem[]> EnsureCheckTemplatesAsync(LandErpDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        CaseCheckTemplateItem[] existing = await db.CaseCheckTemplateItems.Where(item => item.OrganizationId == organizationId)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Title).ToArrayAsync(cancellationToken);
        if (existing.Length > 0) return existing;
        (string Title, CaseCheckLevel Level, string Description)[] defaults =
        [
            ("Собственник", CaseCheckLevel.Quick, "Проверить собственника и основание права."),
            ("Обременения", CaseCheckLevel.Quick, "Проверить зарегистрированные ограничения и обременения."),
            ("Категория / ВРИ", CaseCheckLevel.Quick, "Сверить категорию земли и разрешённое использование."),
            ("Подъезд", CaseCheckLevel.Quick, "Проверить юридический и фактический доступ к участку."),
            ("ПЗЗ / генплан", CaseCheckLevel.Deep, "Проверить применимые градостроительные ограничения."),
            ("Юридическая проверка", CaseCheckLevel.Deep, "Расширенная проверка прав, документов и рисков.")
        ];
        CaseCheckTemplateItem[] created = defaults.Select((value, index) => new CaseCheckTemplateItem
        {
            Id = DataConventions.NewId(), OrganizationId = organizationId, Title = value.Title, Level = value.Level,
            Description = value.Description, SortOrder = (index + 1) * 10, Active = true
        }).ToArray();
        db.CaseCheckTemplateItems.AddRange(created); await db.SaveChangesAsync(cancellationToken); return created;
    }

    private static string NormalizeInspectionAnswer(SiteInspectionItem item, InspectionItemStatus status, string value)
    {
        if (status == InspectionItemStatus.NotChecked) return "";
        if (status != InspectionItemStatus.Answered) return value;
        if (value.Length == 0) throw new ArgumentException($"Укажите ответ для «{item.TitleSnapshot}».");

        return item.AnswerTypeSnapshot switch
        {
            InspectionAnswerType.Boolean => bool.TryParse(value, out bool boolean)
                ? (boolean ? "true" : "false")
                : throw new ArgumentException($"Для «{item.TitleSnapshot}» допустим только ответ Да / Нет."),
            InspectionAnswerType.Number => ParseInspectionNumber(value, item.TitleSnapshot).ToString("G29", CultureInfo.InvariantCulture),
            InspectionAnswerType.Percentage => NormalizePercentage(value, item.TitleSnapshot),
            InspectionAnswerType.Choice => AllowedInspectionChoice(item, value),
            InspectionAnswerType.Text => value,
            _ => throw new ArgumentException("Неизвестный тип ответа пункта осмотра.")
        };
    }

    private static decimal ParseInspectionNumber(string value, string title)
    {
        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out decimal number)
            || decimal.TryParse(value, styles, CultureInfo.GetCultureInfo("ru-RU"), out number))
            return number;
        throw new ArgumentException($"Для «{title}» укажите числовое значение.");
    }

    private static string NormalizePercentage(string value, string title)
    {
        decimal number = ParseInspectionNumber(value, title);
        if (number is < 0 or > 100) throw new ArgumentException($"Для «{title}» укажите процент от 0 до 100.");
        return number.ToString("G29", CultureInfo.InvariantCulture);
    }

    private static string AllowedInspectionChoice(SiteInspectionItem item, string value)
    {
        string[] options = JsonSerializer.Deserialize<string[]>(item.OptionsJsonSnapshot) ?? [];
        return options.Contains(value, StringComparer.Ordinal)
            ? value
            : throw new ArgumentException($"Для «{item.TitleSnapshot}» выберите значение из сохранённых вариантов.");
    }

    private static bool AllowedContentType(CaseAttachmentKind kind, string contentType) => kind switch
    {
        CaseAttachmentKind.Photo => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
        CaseAttachmentKind.Video => contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase),
        CaseAttachmentKind.Audio => contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase),
        CaseAttachmentKind.Document => contentType is "application/pdf" or "text/plain" or "text/csv"
            or "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => false
    };

    private static decimal? Price(decimal? value)
    {
        if (value == null) return null;
        if (value <= 0) throw new ArgumentException("Цена должна быть положительной.");
        return DataConventions.RoundRubles(value.Value);
    }

    private static string NegotiationLabel(CaseNegotiation value) => value.AgreedPrice is { } agreed ? $"Согласованная цена: {agreed:N0} ₽"
        : value.BuyerOffer is { } buyer ? $"Наше предложение: {buyer:N0} ₽"
        : value.SellerPrice is { } seller ? $"Цена продавца: {seller:N0} ₽"
        : value.Outcome.Length > 0 ? value.Outcome : "Событие переговоров";

    private static string NegotiationBody(CaseNegotiation value) => string.Join("\n", new[]
    {
        value.Channel.Length == 0 ? null : "Канал: " + value.Channel,
        value.Contact.Length == 0 ? null : "Контакт: " + value.Contact,
        value.Outcome.Length == 0 ? null : "Результат: " + value.Outcome,
        value.Comment.Length == 0 ? null : value.Comment,
        value.NextStep.Length == 0 ? null : "Следующий шаг: " + value.NextStep
    }.OfType<string>());

    private static Guid? AttachmentOwnerId(CaseAttachment item) => item.OwnerType switch
    {
        CaseAttachmentOwner.Negotiation => item.NegotiationId,
        CaseAttachmentOwner.Check => item.CheckId,
        CaseAttachmentOwner.Inspection => item.InspectionId,
        CaseAttachmentOwner.InspectionItem => item.InspectionItemId,
        _ => null
    };

    private static string AttachmentOwnerLabel(CaseAttachment item, CaseNegotiation[] negotiations, CaseCheck[] checks,
        SiteInspectionItem[] inspectionItems) => item.OwnerType switch
    {
        CaseAttachmentOwner.Case => "Документ всего PropertyCase",
        CaseAttachmentOwner.Negotiation => "Событие переговоров · " + negotiations.First(value => value.Id == item.NegotiationId).EffectiveAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU")),
        CaseAttachmentOwner.Check => "Проверка · " + checks.First(value => value.Id == item.CheckId).Title,
        CaseAttachmentOwner.Inspection => "Осмотр участка",
        CaseAttachmentOwner.InspectionItem => "Пункт осмотра · " + inspectionItems.First(value => value.Id == item.InspectionItemId).TitleSnapshot,
        _ => "PropertyCase"
    };

    private static SourceDiscrepancyView[] Discrepancies(PropertyCase propertyCase,
        IEnumerable<(PropertyCaseSourceLink Link, Listing Item)> sources)
    {
        List<SourceDiscrepancyView> result = [];
        foreach ((_, Listing item) in sources)
        {
            Add(CaseFactField.Title, propertyCase.WorkingTitle, item.Title);
            Add(CaseFactField.Price, Format(propertyCase.WorkingPrice), Format(item.Price));
            Add(CaseFactField.AreaSquareMeters, Format(propertyCase.WorkingAreaSquareMeters), Format(item.AreaSquareMeters));
            Add(CaseFactField.Location, propertyCase.WorkingLocation, item.Location);
            Add(CaseFactField.CadastralNumber, propertyCase.CadastralNumber, item.CadastralNumber);

            void Add(CaseFactField field, string? working, string? source)
            {
                if (source == null) return;
                string current = working ?? "—";
                result.Add(new(item.Id, item.Source, field, current, source,
                    !string.Equals(current, source, StringComparison.OrdinalIgnoreCase)));
            }
        }
        return result.ToArray();
    }

    private static (string? Text, decimal? Number) NormalizeCorrection(CorrectPropertyCaseFact command)
    {
        return command.Field switch
        {
            CaseFactField.Title when command.NumericValue == null
                => (Required(command.TextValue, 1, 20000, "Укажите название объекта."), null),
            CaseFactField.Price when command.TextValue == null && command.NumericValue is null
                => (null, null),
            CaseFactField.Price when command.TextValue == null && command.NumericValue > 0
                => (null, DataConventions.RoundRubles(command.NumericValue.Value)),
            CaseFactField.AreaSquareMeters when command.TextValue == null && command.NumericValue is null
                => (null, null),
            CaseFactField.AreaSquareMeters when command.TextValue == null && command.NumericValue > 0
                => (null, decimal.Round(command.NumericValue.Value, 4, MidpointRounding.ToEven)),
            CaseFactField.Location when command.NumericValue == null
                => (Optional(command.TextValue, 20000), null),
            CaseFactField.CadastralNumber when command.NumericValue == null
                => (Optional(command.TextValue, 128), null),
            CaseFactField.Price => throw new ArgumentException("Цена должна быть больше нуля или очищена."),
            CaseFactField.AreaSquareMeters => throw new ArgumentException("Площадь должна быть больше нуля или очищена."),
            _ => throw new ArgumentException("Передано значение неподходящего типа.")
        };
    }

    private static void ApplyCorrection(PropertyCase propertyCase, CaseFactField field, string? textValue, decimal? numericValue)
    {
        switch (field)
        {
            case CaseFactField.Title:
                propertyCase.WorkingTitle = textValue!;
                break;
            case CaseFactField.Price:
                propertyCase.WorkingPrice = numericValue;
                break;
            case CaseFactField.AreaSquareMeters:
                propertyCase.WorkingAreaSquareMeters = numericValue;
                break;
            case CaseFactField.Location:
                propertyCase.WorkingLocation = textValue;
                break;
            case CaseFactField.CadastralNumber:
                propertyCase.CadastralNumber = textValue;
                break;
            default:
                throw new ArgumentException("Поле не поддерживается.");
        }
    }

    private static string CaseFactValue(PropertyCase propertyCase, CaseFactField field) => field switch
    {
        CaseFactField.Title => propertyCase.WorkingTitle,
        CaseFactField.Price => Format(propertyCase.WorkingPrice) ?? "—",
        CaseFactField.AreaSquareMeters => Format(propertyCase.WorkingAreaSquareMeters) ?? "—",
        CaseFactField.Location => propertyCase.WorkingLocation ?? "—",
        CaseFactField.CadastralNumber => propertyCase.CadastralNumber ?? "—",
        _ => throw new ArgumentException("Поле не поддерживается.")
    };

    private static string CaseFactLabel(CaseFactField field) => field switch
    {
        CaseFactField.Title => "Название",
        CaseFactField.Price => "Цена",
        CaseFactField.AreaSquareMeters => "Площадь",
        CaseFactField.Location => "Локация",
        CaseFactField.CadastralNumber => "Кадастровый номер",
        _ => field.ToString()
    };

    private static string TimelineFactValue(string value) =>
        value.Length <= 2000 ? value : value[..1999] + "…";

    private static string ApplyFact(PropertyCase propertyCase, Listing source, CaseFactField field)
    {
        return field switch
        {
            CaseFactField.Title when !string.IsNullOrWhiteSpace(source.Title) => Set(source.Title, value => propertyCase.WorkingTitle = value),
            CaseFactField.Price when source.Price != null => Set(Format(source.Price)!, _ => propertyCase.WorkingPrice = source.Price),
            CaseFactField.AreaSquareMeters when source.AreaSquareMeters != null => Set(Format(source.AreaSquareMeters)!, _ => propertyCase.WorkingAreaSquareMeters = source.AreaSquareMeters),
            CaseFactField.Location when !string.IsNullOrWhiteSpace(source.Location) => Set(source.Location, value => propertyCase.WorkingLocation = value),
            CaseFactField.CadastralNumber when !string.IsNullOrWhiteSpace(source.CadastralNumber) => Set(source.CadastralNumber, value => propertyCase.CadastralNumber = value),
            _ => throw new ArgumentException("Источник не содержит выбранное значение.")
        };

        static string Set(string value, Action<string> setter) { setter(value); return value; }
    }

    private static string? Format(decimal? value) => value?.ToString("0.####", CultureInfo.InvariantCulture);

    private async Task<PropertyCase> CreatePropertyCaseAsync(LandErpDbContext db, AccessContext context, string title,
        decimal? price, decimal? areaSquareMeters, string? location, string? cadastralNumber, string provenance,
        string transitionAction, CancellationToken cancellationToken, string currency = "RUB", long reviewedDataRevision = 0)
    {
        await using var number = db.Database.GetDbConnection().CreateCommand();
        number.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        number.CommandText = "SELECT nextval('procurement.property_case_numbers')";
        long businessNumber = (long)(await number.ExecuteScalarAsync(cancellationToken))!;
        Guid caseId = DataConventions.NewId();
        DateTimeOffset now = time.GetUtcNow();
        Assignment assignment = new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
            ObjectId = caseId, EmployeeId = context.EmployeeId
        };
        WorkTask task = new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
            ObjectId = caseId, EmployeeId = context.EmployeeId, Title = "Первичный анализ", RecordedAt = now
        };
        PropertyCase propertyCase = new()
        {
            Id = caseId,
            OrganizationId = context.OrganizationId,
            BusinessNumber = "PC-" + businessNumber.ToString("D6", CultureInfo.InvariantCulture),
            WorkingTitle = title,
            WorkingPrice = price,
            Currency = currency,
            WorkingAreaSquareMeters = areaSquareMeters,
            WorkingLocation = location,
            CadastralNumber = cadastralNumber,
            FactsProvenance = provenance,
            DepartmentId = context.DepartmentId,
            TeamId = context.TeamId,
            ManagerEmployeeId = context.EmployeeId,
            AssignmentId = assignment.Id,
            WorkTaskId = task.Id,
            ReviewedDataRevision = reviewedDataRevision,
            RecordedAt = now
        };
        db.WorkAssignments.Add(assignment);
        db.WorkTasks.Add(task);
        db.PropertyCases.Add(propertyCase);
        db.CaseDocumentRequirements.AddRange(DefaultDocumentRequirements(propertyCase, context.EmployeeId, now));
        db.WorkflowTransitions.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
            ObjectId = caseId, FromStageId = "new", ToStageId = "analysis", Action = transitionAction,
            ActorEmployeeId = context.EmployeeId, ObjectVersion = 1, RecordedAt = now
        });
        return propertyCase;
    }

    private static string NextActionTypeLabel(WorkTaskType type) => type switch
    {
        WorkTaskType.Call => "Звонок",
        WorkTaskType.Meeting => "Встреча",
        WorkTaskType.Check => "Проверка",
        WorkTaskType.Documents => "Документы",
        WorkTaskType.Decision => "Решение",
        _ => "Другое"
    };

    private static CaseDocumentRequirement[] DefaultDocumentRequirements(PropertyCase propertyCase, Guid employeeId, DateTimeOffset recordedAt)
    {
        (string Code, string Title, string Description, string Source)[] defaults =
        [
            ("egrn", "Выписка ЕГРН", "Актуальные сведения о правах, правообладателях и ограничениях.", "Росреестр"),
            ("owner_identity", "Документы собственника", "Документы для идентификации собственника или его представителя.", "Продавец"),
            ("title_basis", "Документ-основание права", "Основание возникновения права для глубокой юридической проверки.", "Продавец"),
            ("access_scheme", "Схема подъезда / сервитут", "Правовое и фактическое основание доступа к участку.", "Продавец"),
            ("cadastral_plan", "Кадастровый план", "Границы, конфигурация и кадастровые сведения об участке.", "Росреестр")
        ];
        return defaults.Select(item => new CaseDocumentRequirement
        {
            Id = DataConventions.NewId(), OrganizationId = propertyCase.OrganizationId, PropertyCaseId = propertyCase.Id,
            Code = item.Code, Title = item.Title, Description = item.Description, ExpectedSource = item.Source,
            Status = CaseDocumentStatus.Missing, UpdatedByEmployeeId = employeeId, UpdatedAt = recordedAt
        }).ToArray();
    }

    private static string DocumentStatusLabel(CaseDocumentStatus status) => status switch
    {
        CaseDocumentStatus.Missing => "не получен",
        CaseDocumentStatus.Requested => "запрошен",
        CaseDocumentStatus.Received => "получен",
        CaseDocumentStatus.Verified => "проверен",
        _ => status.ToString()
    };

    private static Task<DecisionTarget[]> TargetsAsync(LandErpDbContext db, PropertyCase propertyCase,
        Guid currentCaseAssigneeId, string permission, ProcurementRecipientAccess recipientAccess,
        CancellationToken cancellationToken)
    {
        IQueryable<EmployeeAssignment> eligible = ProcurementVisibility.EligibleRecipientAssignments(
            db, propertyCase, currentCaseAssigneeId, recipientAccess);
        var query =
            from assignment in eligible
            join employee in db.Employees on assignment.EmployeeId equals employee.Id
            where db.RolePermissions.Any(grant => grant.RoleId == assignment.RoleId && grant.PermissionId == Permissions.QueueRead)
                && db.RolePermissions.Any(grant => grant.RoleId == assignment.RoleId && grant.PermissionId == permission)
            select new { employee.Id, employee.DisplayName };
        return query.Distinct().OrderBy(item => item.DisplayName)
            .Select(item => new DecisionTarget(item.Id, item.DisplayName)).ToArrayAsync(cancellationToken);
    }

    private static Task<DecisionTarget[]> DossierTargetsAsync(LandErpDbContext db, PropertyCase propertyCase,
        Guid currentCaseAssigneeId, ProcurementRecipientAccess recipientAccess, CancellationToken cancellationToken)
    {
        IQueryable<EmployeeAssignment> eligible = ProcurementVisibility.EligibleRecipientAssignments(
            db, propertyCase, currentCaseAssigneeId, recipientAccess);
        var query =
            from assignment in eligible
            join employee in db.Employees on assignment.EmployeeId equals employee.Id
            where db.RolePermissions.Any(grant => grant.RoleId == assignment.RoleId && grant.PermissionId == Permissions.QueueRead)
                && (db.RolePermissions.Any(grant => grant.RoleId == assignment.RoleId && grant.PermissionId == Permissions.ManagerDecide)
                    || db.RolePermissions.Any(grant => grant.RoleId == assignment.RoleId && grant.PermissionId == Permissions.HeadDecide))
            select new { employee.Id, employee.DisplayName };
        return query.Distinct().OrderBy(item => item.DisplayName)
            .Select(item => new DecisionTarget(item.Id, item.DisplayName)).ToArrayAsync(cancellationToken);
    }

    private static async Task<Dictionary<Guid, List<(PropertyCaseSourceLink Link, Listing Item)>>> LoadSourcesAsync(
        LandErpDbContext db, Guid[] caseIds, CancellationToken cancellationToken)
    {
        var values = await (from link in db.PropertyCaseSourceLinks
                            join item in db.Listings on link.CatalogItemId equals item.Id
                            where caseIds.Contains(link.PropertyCaseId) && link.Confirmed
                            select new { Link = link, Item = item }).ToArrayAsync(cancellationToken);
        return values.GroupBy(value => value.Link.PropertyCaseId)
            .ToDictionary(group => group.Key, group => group.Select(value => (value.Link, value.Item)).ToList());
    }

    private static QueueItem Item(Row row, Dictionary<Guid, string> names, List<(PropertyCaseSourceLink Link, Listing Item)> sources) => new(
        row.Case.Id, row.Case.BusinessNumber, row.Case.WorkingTitle, sources.Select(item => item.Item.Source).Distinct().ToArray(),
        row.Case.WorkingPrice, row.Case.Currency, row.Case.WorkingAreaSquareMeters, row.Case.WorkingLocation, row.Case.StageId,
        names.GetValueOrDefault(row.Assignment.EmployeeId, "Сотрудник"), row.Task.DueAt,
        row.Case.StageId == "returned" ? "Руководитель вернул: требуются исправления" : SourcesChanged(sources) ? "Источник изменился" : "Рабочий объект закупки",
        new[] { row.Case.WorkingPrice == null ? "цена" : null, row.Case.WorkingAreaSquareMeters == null ? "площадь" : null, row.Case.WorkingLocation == null ? "местоположение" : null }.OfType<string>().ToArray(),
        SourcesChanged(sources), SourceRevision(sources), row.Case.Version,
        row.Task.Title, row.Task.Description, row.Task.Version);

    private static bool SourcesChanged(IEnumerable<(PropertyCaseSourceLink Link, Listing Item)> sources) => sources.Any(value => value.Item.DataRevision > value.Link.ReviewedDataRevision);
    private static long SourceRevision(IEnumerable<(PropertyCaseSourceLink Link, Listing Item)> sources) => sources.Sum(value => value.Item.DataRevision);
    private static async Task<Guid> LinkSameObjectAsync(LandErpDbContext db, Guid organizationId,
        Listing left, Listing right, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guid[] groupIds = new[] { left.ObjectGroupId, right.ObjectGroupId }.OfType<Guid>().Distinct().ToArray();
        Listing[] members = groupIds.Length == 0
            ? [left, right]
            : await db.Listings.Where(item => item.OrganizationId == organizationId
                    && (item.Id == left.Id || item.Id == right.Id
                        || item.ObjectGroupId != null && groupIds.Contains(item.ObjectGroupId.Value)))
                .OrderBy(item => item.ReceivedAt).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
        Guid[] memberIds = members.Select(item => item.Id).Distinct().ToArray();
        Guid[] caseIds = await db.PropertyCaseSourceLinks.Where(item =>
                memberIds.Contains(item.CatalogItemId) && item.Confirmed)
            .Select(item => item.PropertyCaseId).Distinct().ToArrayAsync(cancellationToken);
        if (caseIds.Length > 1)
            throw new ArgumentException("Нельзя объединить объявления: они уже относятся к разным PropertyCase.");

        if (left.ObjectGroupId is Guid sameGroup && right.ObjectGroupId == sameGroup)
            return sameGroup;

        CatalogObjectGroup group;
        if (groupIds.Length == 0)
        {
            group = new()
            {
                Id = DataConventions.NewId(), OrganizationId = organizationId,
                RecordedAt = now, UpdatedAt = now
            };
            db.CatalogObjectGroups.Add(group);
        }
        else
        {
            CatalogObjectGroup[] existingGroups = await db.CatalogObjectGroups.Where(item =>
                    item.OrganizationId == organizationId && groupIds.Contains(item.Id))
                .OrderBy(item => item.RecordedAt).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
            if (existingGroups.Length != groupIds.Length) throw new DbUpdateConcurrencyException();
            group = existingGroups[0];
            group.UpdatedAt = now;
            db.Entry(group).Property(item => item.Version).IsModified = true;
            foreach (CatalogObjectGroup obsolete in existingGroups.Skip(1))
            {
                foreach (Listing member in members.Where(item => item.ObjectGroupId == obsolete.Id))
                    member.ObjectGroupId = group.Id;
                db.CatalogObjectGroups.Remove(obsolete);
            }
        }

        left.ObjectGroupId = group.Id;
        right.ObjectGroupId = group.Id;

        CatalogDuplicateCandidate[] resolvedInsideGroup = await db.CatalogDuplicateCandidates
            .Where(item => item.OrganizationId == organizationId
                && item.Status == DuplicateCandidateStatus.Pending
                && memberIds.Contains(item.ListingId) && memberIds.Contains(item.CandidateListingId))
            .ToArrayAsync(cancellationToken);
        foreach (CatalogDuplicateCandidate candidate in resolvedInsideGroup)
        {
            candidate.Status = DuplicateCandidateStatus.Obsolete;
            candidate.UpdatedAt = now;
        }
        return group.Id;
    }

    private static async Task RejectSameObjectPairAsync(LandErpDbContext db, Guid employeeId,
        Listing left, Listing right, DateTimeOffset now, CancellationToken cancellationToken)
    {
        CatalogDuplicateCandidate? candidate = await db.CatalogDuplicateCandidates.SingleOrDefaultAsync(item =>
            item.OrganizationId == left.OrganizationId
            && ((item.ListingId == left.Id && item.CandidateListingId == right.Id)
                || (item.ListingId == right.Id && item.CandidateListingId == left.Id)), cancellationToken);
        if (candidate == null)
        {
            db.CatalogDuplicateCandidates.Add(new()
            {
                Id = DataConventions.NewId(), OrganizationId = left.OrganizationId,
                ListingId = left.Id, CandidateListingId = right.Id, Score = 0,
                ReasonsJson = JsonSerializer.Serialize<string[]>(["Разделено менеджером: это разные физические объекты"]),
                Status = DuplicateCandidateStatus.Rejected, ReviewedByEmployeeId = employeeId,
                RecordedAt = now, UpdatedAt = now, ReviewedAt = now
            });
            return;
        }

        candidate.Status = DuplicateCandidateStatus.Rejected;
        candidate.ReviewedByEmployeeId = employeeId;
        candidate.ReviewedAt = now;
        candidate.UpdatedAt = now;
    }

    private static async Task PromoteForIndependentReviewAsync(LandErpDbContext db,
        Listing listing, DateTimeOffset now, CancellationToken cancellationToken)
    {
        bool hasCase = await db.PropertyCaseSourceLinks.AnyAsync(item =>
            item.CatalogItemId == listing.Id && item.Confirmed, cancellationToken);
        if (hasCase || listing.Disposition != CatalogDisposition.Duplicate) return;
        listing.Disposition = CatalogDisposition.Incoming;
        listing.AttentionRequired = true;
        listing.AttentionAt = now;
        listing.QueueReason = "Объявление снова самостоятельное после разделения группы.";
        listing.ChangedAt = now;
    }

    private static CatalogItemView CatalogView(Listing item, Guid? caseId, string? businessNumber, string? caseStage,
        int objectGroupMemberCount = 0) => new(
        item.Id, item.Source, item.ExternalId, item.Url, item.Title ?? "Название неизвестно", item.Price,
        PricePerSotka(item.Price, item.AreaSquareMeters), item.Currency, item.AreaSquareMeters, item.Location,
        item.CadastralNumber, item.Description, item.Provenance, item.IngestionKind, item.Disposition,
        item.QueueReason, item.AttentionRequired, item.ReceivedAt, item.ChangedAt, item.LastObservedAt,
        caseId, businessNumber, caseStage, caseStage is "rejected" or "monitor", item.Version,
        item.ObjectGroupId, objectGroupMemberCount);
    private static decimal? PricePerSotka(decimal? price, decimal? areaSquareMeters) => price is > 0 && areaSquareMeters is > 0
        ? decimal.Round(price.Value * 100m / areaSquareMeters.Value, 4, MidpointRounding.ToEven) : null;
    private static CatalogEvent CatalogEvent(Listing item, CatalogEventKind kind, string message, DateTimeOffset recordedAt) => new()
    {
        Id = DataConventions.NewId(), OrganizationId = item.OrganizationId, CatalogItemId = item.Id,
        Kind = kind, Message = message, ObservedPrice = item.Price,
        ObservedPricePerSotka = PricePerSotka(item.Price, item.AreaSquareMeters), RecordedAt = recordedAt
    };
    private static string DispositionLabel(CatalogDisposition value) => value switch
    {
        CatalogDisposition.Dismissed => "Не подходит",
        CatalogDisposition.Duplicate => "Дубль",
        CatalogDisposition.Fake => "Фейк",
        CatalogDisposition.RemovedAtSource => "Снято",
        CatalogDisposition.Sold => "Продано",
        _ => value.ToString()
    };
    private static void ValidatePage(string text, int offset, int size)
    { if (text.Length > 200 || offset < 0 || size is < 1 or > 100) throw new ArgumentException("FILTER_INVALID"); }
    private static string Required(string? value, int min, int max, string message)
    { string result = value?.Trim() ?? ""; return result.Length < min || result.Length > max ? throw new ArgumentException(message) : result; }
    private static string? Optional(string? value, int max)
    { string? result = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); return result?.Length > max ? throw new ArgumentException("Значение слишком длинное.") : result; }
    public static string ActionLabel(ProcurementAction action, bool head) => action switch
    {
        ProcurementAction.Monitor => head ? "Руководитель: наблюдать" : "Наблюдать",
        ProcurementAction.Clarify => "Уточнить",
        ProcurementAction.Reject => head ? "Руководитель отклонил" : "Отклонить",
        ProcurementAction.Forward => "Передан руководителю",
        ProcurementAction.Return => "Возвращён менеджеру",
        _ => "Дальнейшая работа одобрена"
    };
}
