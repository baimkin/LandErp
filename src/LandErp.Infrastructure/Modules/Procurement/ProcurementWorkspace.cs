using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace LandErp.Infrastructure.Modules.Procurement;

/// <summary>Catalog is organization-shared; procurement access is derived only from case responsibility.</summary>
public sealed class ProcurementWorkspace(IDbContextFactory<LandErpDbContext> factory, IAccessControl access, TimeProvider time)
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
        var rows = from item in db.PropertyCases
                   join assignment in db.WorkAssignments on item.AssignmentId equals assignment.Id
                   join task in db.WorkTasks on item.WorkTaskId equals task.Id
                   where item.OrganizationId == context.OrganizationId
                   select new Row { Case = item, Assignment = assignment, Task = task };
        return context.Scope switch
        {
            AccessScope.Organization => rows,
            AccessScope.Department => rows.Where(row => context.DepartmentId != null && row.Case.DepartmentId == context.DepartmentId),
            AccessScope.Team => rows.Where(row => context.TeamId != null && row.Case.TeamId == context.TeamId),
            AccessScope.AssignedObjects => rows.Where(row => row.Assignment.EmployeeId == context.EmployeeId),
            _ => rows.Where(row => row.Case.ManagerEmployeeId == context.EmployeeId)
        };
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
        return new(items.Select(item =>
        {
            byItem.TryGetValue(item.Id, out var link);
            return CatalogView(item, link?.Id, link?.BusinessNumber, link?.StageId);
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
        CatalogEventView[] events = await db.CatalogEvents.AsNoTracking().Where(value => value.CatalogItemId == item.Id)
            .OrderByDescending(value => value.RecordedAt).ThenByDescending(value => value.Id).Take(100)
            .Select(value => new CatalogEventView(value.Id, value.Kind, value.Message, value.ObservedPrice,
                value.ObservedPricePerSotka, value.RecordedAt)).ToArrayAsync(cancellationToken);
        return new(CatalogView(item, link?.Id, link?.BusinessNumber, link?.StageId), item.SellerName, item.IngressComment,
            new(item.TargetTotalPrice, item.TargetPricePerSotka, item.MonitoringStartedAt, item.LastEvaluatedPrice,
                item.LastEvaluatedPricePerSotka, item.LastEvaluatedAt), events);
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
        db.Listings.Add(item);
        OrganizationWorkspace.AddAudit(db, context, subject, "CatalogItemCreatedManually", "CatalogItem", item.Id,
            new { item.Source, item.ExternalId, HasUrl = item.Url != null }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    public async Task SetDispositionAsync(Subject subject, SetCatalogDisposition command, string correlationId, CancellationToken cancellationToken)
    {
        if (command.Disposition is CatalogDisposition.InWork or CatalogDisposition.Monitoring or CatalogDisposition.Incoming)
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

    public async Task<IReadOnlyList<CaseLinkTarget>> ReadLinkTargetsAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await VisibleCases(db, context).OrderByDescending(row => row.Case.RecordedAt).Take(100)
            .Select(row => new CaseLinkTarget(row.Case.Id, row.Case.BusinessNumber, row.Case.WorkingTitle)).ToArrayAsync(cancellationToken);
    }

    public async Task<TakeToWorkResult> TakeToWorkAsync(Subject subject, TakeCatalogItemToWork command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Listing catalogItem = (await db.Listings.FromSqlInterpolated(
            $"SELECT * FROM catalog.listings WHERE id={command.CatalogItemId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw new AccessDeniedException();
        PropertyCaseSourceLink? existingLink = await db.PropertyCaseSourceLinks.SingleOrDefaultAsync(
            item => item.CatalogItemId == catalogItem.Id && item.Confirmed, cancellationToken);
        if (existingLink != null)
        {
            PropertyCase existing = await VisibleCases(db, context).Where(row => row.Case.Id == existingLink.PropertyCaseId)
                .Select(row => row.Case).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
            await transaction.CommitAsync(cancellationToken);
            return new(existing.Id, existing.BusinessNumber, false);
        }

        PropertyCase propertyCase;
        bool created = command.ExistingCaseId == null;
        if (created)
        {
            await using var number = db.Database.GetDbConnection().CreateCommand();
            number.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            number.CommandText = "SELECT nextval('procurement.property_case_numbers')";
            long businessNumber = (long)(await number.ExecuteScalarAsync(cancellationToken))!;
            Guid caseId = DataConventions.NewId();
            Assignment assignment = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = caseId, EmployeeId = context.EmployeeId };
            WorkTask task = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = caseId, EmployeeId = context.EmployeeId, Title = "Первичный анализ", RecordedAt = time.GetUtcNow() };
            propertyCase = new()
            {
                Id = caseId,
                OrganizationId = context.OrganizationId,
                BusinessNumber = "PC-" + businessNumber.ToString("D6", System.Globalization.CultureInfo.InvariantCulture),
                WorkingTitle = catalogItem.Title ?? "Объект без названия",
                WorkingPrice = catalogItem.Price,
                Currency = catalogItem.Currency,
                WorkingAreaSquareMeters = catalogItem.AreaSquareMeters,
                WorkingLocation = catalogItem.Location,
                CadastralNumber = catalogItem.CadastralNumber,
                FactsProvenance = "Catalog snapshot at case creation",
                DepartmentId = context.DepartmentId,
                TeamId = context.TeamId,
                ManagerEmployeeId = context.EmployeeId,
                AssignmentId = assignment.Id,
                WorkTaskId = task.Id,
                ReviewedDataRevision = catalogItem.DataRevision,
                RecordedAt = time.GetUtcNow()
            };
            db.WorkAssignments.Add(assignment); db.WorkTasks.Add(task); db.PropertyCases.Add(propertyCase);
            db.WorkflowTransitions.Add(new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = caseId, FromStageId = "new", ToStageId = "analysis", Action = "TakeWork", ActorEmployeeId = context.EmployeeId, ObjectVersion = 1, RecordedAt = time.GetUtcNow() });
        }
        else
        {
            Guid existingCaseId = command.ExistingCaseId.GetValueOrDefault();
            propertyCase = await VisibleCases(db, context).Where(row => row.Case.Id == existingCaseId)
                .Select(row => row.Case).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
        }

        db.PropertyCaseSourceLinks.Add(new()
        {
            Id = DataConventions.NewId(),
            OrganizationId = context.OrganizationId,
            PropertyCaseId = propertyCase.Id,
            CatalogItemId = catalogItem.Id,
            Confirmed = true,
            RelationType = "Source",
            ActorEmployeeId = context.EmployeeId,
            Provenance = "User confirmed",
            ReviewedDataRevision = catalogItem.DataRevision,
            RecordedAt = time.GetUtcNow()
        });
        catalogItem.Disposition = CatalogDisposition.InWork;
        catalogItem.AttentionRequired = false;
        db.Entry(catalogItem).Property(value => value.Version).IsModified = true;
        string title = created ? "Взят в работу" : "Добавлен источник";
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(),
            OrganizationId = context.OrganizationId,
            ObjectType = "PropertyCase",
            ObjectId = propertyCase.Id,
            ActorEmployeeId = context.EmployeeId,
            Kind = created ? "Decision" : "SourceLinked",
            Title = title,
            Body = $"{catalogItem.Source}: {catalogItem.Title ?? "источник"}",
            RecordedAt = time.GetUtcNow()
        });
        OrganizationWorkspace.AddAudit(db, context, subject, created ? "CatalogItemTakenToWork" : "CatalogItemLinkedToCase",
            "PropertyCase", propertyCase.Id, new { CatalogItemId = catalogItem.Id, propertyCase.BusinessNumber }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(propertyCase.Id, propertyCase.BusinessNumber, created);
    }

    public async Task<TakeToWorkResult> ResumeCaseAsync(Subject subject, ResumeCatalogItemCase command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.ManagerDecide, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
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
        else query = query.Where(row => row.Case.StageId != "rejected" && row.Case.StageId != "approved"
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
        DecisionTarget[] heads = await TargetsAsync(db, row.Case, Permissions.HeadDecide, cancellationToken);
        DecisionTarget[] managers = await TargetsAsync(db, row.Case, Permissions.ManagerDecide, cancellationToken);
        bool manager = await AllowedAsync(subject, Permissions.ManagerDecide, cancellationToken) && row.Assignment.EmployeeId == context.EmployeeId
            && row.Case.StageId != "pending_head" && (row.Case.StageId is not ("approved" or "rejected") || SourcesChanged(sources));
        bool head = await AllowedAsync(subject, Permissions.HeadDecide, cancellationToken) && row.Case.StageId == "pending_head"
            && row.Assignment.EmployeeId == context.EmployeeId && row.Case.ManagerEmployeeId != context.EmployeeId;
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
            heads.Where(item => item.EmployeeId != context.EmployeeId).ToArray(), managers, row.Case.ManagerEmployeeId, manager, head);
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
        PropertyCase propertyCase = (await db.PropertyCases.FromSqlInterpolated(
            $"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={read.OrganizationId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw new AccessDeniedException();
        Row row = await VisibleCases(db, read).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        List<(PropertyCaseSourceLink Link, Listing Item)> sources = (await LoadSourcesAsync(db, [propertyCase.Id], cancellationToken)).GetValueOrDefault(propertyCase.Id, []);
        long sourceRevision = SourceRevision(sources);
        if (propertyCase.Version != command.ExpectedCaseVersion || sourceRevision != command.ExpectedSourceRevision) throw new DbUpdateConcurrencyException();
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
        string stage = command.Action switch { ProcurementAction.Monitor => "monitor", ProcurementAction.Clarify => "clarify", ProcurementAction.Reject => "rejected", ProcurementAction.Forward => "pending_head", ProcurementAction.Return => "returned", _ => "approved" };
        Guid target = context.EmployeeId;
        if (command.Action == ProcurementAction.Forward)
        {
            target = command.TargetEmployeeId ?? throw new ArgumentException("Выберите руководителя закупки.");
            if (target == context.EmployeeId || !(await TargetsAsync(db, propertyCase, Permissions.HeadDecide, cancellationToken)).Any(value => value.EmployeeId == target)) throw new AccessDeniedException();
            propertyCase.PendingApprovalId = DataConventions.NewId();
        }
        if (headAction)
        {
            if (command.Action == ProcurementAction.Approve && SourcesChanged(sources)) throw new ArgumentException("Источники изменились после передачи. Верните объект менеджеру для обновления анализа.");
            target = command.Action == ProcurementAction.Return ? command.TargetEmployeeId ?? propertyCase.ManagerEmployeeId : propertyCase.ManagerEmployeeId;
            if (!(await TargetsAsync(db, propertyCase, Permissions.ManagerDecide, cancellationToken)).Any(value => value.EmployeeId == target)) throw new AccessDeniedException();
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
        row.Task.Completed = stage is "approved" or "rejected";
        row.Task.Title = stage switch { "pending_head" => "Рассмотреть первичный анализ", "returned" => "Исправить / уточнить первичный анализ", "clarify" => "Уточнить данные объекта", "monitor" => "Наблюдать за объектом", "approved" => "Дальнейшая работа одобрена", "rejected" => "Объект отклонён", _ => "Первичный анализ" };
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
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(value => value.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        if (row.Assignment.EmployeeId != context.EmployeeId) throw new AccessDeniedException();
        await access.RequireAsync(subject, row.Case.StageId == "pending_head" ? Permissions.HeadDecide : Permissions.ManagerDecide, cancellationToken);
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
        OrganizationWorkspace.AddAudit(db, context, subject, command.Contact ? "SellerContactRecorded" : "CaseNoteAdded", "PropertyCase", row.Case.Id,
            new { command.Contact, command.EffectiveAt }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
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

    private async Task<bool> AllowedAsync(Subject subject, string permission, CancellationToken cancellationToken)
    { try { await access.RequireAsync(subject, permission, cancellationToken); return true; } catch (AccessDeniedException) { return false; } }

    private static Task<DecisionTarget[]> TargetsAsync(LandErpDbContext db, PropertyCase propertyCase, string permission, CancellationToken cancellationToken)
    {
        var query = from employee in db.Employees
                    join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
                    join grant in db.RolePermissions on assignment.RoleId equals grant.RoleId
                    where employee.OrganizationId == propertyCase.OrganizationId && employee.Active && grant.PermissionId == permission
                        && (assignment.Scope == AccessScope.Organization || assignment.Scope == AccessScope.Own || assignment.Scope == AccessScope.AssignedObjects
                            || assignment.Scope == AccessScope.Department && propertyCase.DepartmentId != null && assignment.OrgUnitId == propertyCase.DepartmentId
                            || assignment.Scope == AccessScope.Team && propertyCase.TeamId != null && assignment.TeamId == propertyCase.TeamId)
                    select new { employee.Id, employee.DisplayName };
        return query.Distinct().OrderBy(item => item.DisplayName).Select(item => new DecisionTarget(item.Id, item.DisplayName)).ToArrayAsync(cancellationToken);
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
        SourcesChanged(sources), SourceRevision(sources), row.Case.Version);

    private static bool SourcesChanged(IEnumerable<(PropertyCaseSourceLink Link, Listing Item)> sources) => sources.Any(value => value.Item.DataRevision > value.Link.ReviewedDataRevision);
    private static long SourceRevision(IEnumerable<(PropertyCaseSourceLink Link, Listing Item)> sources) => sources.Sum(value => value.Item.DataRevision);
    private static CatalogItemView CatalogView(Listing item, Guid? caseId, string? businessNumber, string? caseStage) => new(
        item.Id, item.Source, item.ExternalId, item.Url, item.Title ?? "Название неизвестно", item.Price,
        PricePerSotka(item.Price, item.AreaSquareMeters), item.Currency, item.AreaSquareMeters, item.Location,
        item.CadastralNumber, item.Description, item.Provenance, item.IngestionKind, item.Disposition,
        item.QueueReason, item.AttentionRequired, item.ReceivedAt, item.ChangedAt, item.LastObservedAt,
        caseId, businessNumber, caseStage, caseStage is "rejected" or "monitor", item.Version);
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
