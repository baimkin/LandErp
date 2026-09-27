using LandErp.Infrastructure.Modules.Catalog;
using System.Globalization;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Overview;

public sealed class OverviewService(
    IDbContextFactory<LandErpDbContext> factory,
    IEmployeeAccessService employeeAccess,
    TimeProvider time) : IOverviewService
{
    public OverviewService(IDbContextFactory<LandErpDbContext> factory, TimeProvider time)
        : this(factory, new EmployeeAccessService(factory), time) { }

    private static readonly IncomingLandType[] DefaultTypes = Enum.GetValues<IncomingLandType>();
    private static readonly int[] AllowedPeriods = [7, 30, 90, 180];
    private const int CompactMarketSize = 5;
    private const int FeedSize = 5;
    private const int MarketSortCandidateLimit = 500;

    private sealed record CaseTaskRow(PropertyCase Case, WorkTask Task, Guid AssigneeId,
        bool HasCheckIssue, bool SourceChanged);
    private sealed record GroupDb(Guid Id, string Name, int SortOrder, SearchGroupMarketSettings? Settings);

    public async Task<OverviewView> ReadAsync(Subject subject, CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await employeeAccess.ResolveAsync(subject, cancellationToken);
        AccessContext identity = effective.OrganizationContext;
        AccessContext? queue = effective.CanReadProcurement ? effective.ProcurementReadContext : null;
        bool canManage = effective.CanProcessIncoming;
        AccessContext? collection = effective.CanReadCollection ? effective.OrganizationContext : null;
        DateTimeOffset now = time.GetUtcNow();
        (DateTimeOffset todayStart, DateTimeOffset tomorrowStart) = TodayBounds(now);

        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        int newIncoming = 0;
        int receivedToday = 0;
        int activeCount = 0;
        int waitingCount = 0;
        int overdueCount = 0;
        int deepCount = 0;
        int acquiredCount = 0;
        int procurementAttentionCount = 0;
        List<OverviewAttentionItem> attentionItems = [];
        List<OverviewWorkItem> myWork = [];
        int myWorkTotal = 0;
        List<OverviewTeamMember> team = [];
        int teamTotal = 0;
        MarketGroupPage market = new([], 0, 0, CompactMarketSize);

        if (queue != null)
        {
            IQueryable<PropertyCase> visible = ProcurementVisibility.ApplyRead(
                db.PropertyCases.AsNoTracking(), db, effective);
            IQueryable<PropertyCase> active = visible.Where(item => item.StageId != "rejected" && item.StageId != "acquired");
            var caseTasks = from item in active
                            from task in db.WorkTasks.AsNoTracking()
                                .Where(t => t.OrganizationId == item.OrganizationId && t.ObjectType == "PropertyCase" && t.ObjectId == item.Id
                                    && ((!t.Completed && !t.Deleted) || t.Id == item.WorkTaskId))
                                .OrderBy(t => t.Completed || t.Deleted).ThenByDescending(t => t.DueAt < (t.DueHasTime ? now : todayStart))
                                .ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenBy(t => t.RecordedAt).ThenBy(t => t.Id).Take(1)
                            join assignment in db.WorkAssignments.AsNoTracking() on item.AssignmentId equals assignment.Id
                            select new
                            {
                                Case = item,
                                Task = task,
                                AssigneeId = assignment.EmployeeId,
                                HasCheckIssue = db.CaseChecks.Any(check => check.PropertyCaseId == item.Id
                                    && (check.Status == CaseCheckStatus.Issue || check.Status == CaseCheckStatus.Blocked || check.Blocker)),
                                SourceChanged = db.PropertyCaseSourceLinks.Any(link => link.PropertyCaseId == item.Id && link.Confirmed
                                    && db.Listings.Any(source => source.Id == link.CatalogItemId
                                        && source.DataRevision > link.ReviewedDataRevision))
                            };

            if (effective.CanReadIncoming)
            {
                newIncoming = await db.Listings.AsNoTracking().CountAsync(item => item.OrganizationId == queue.OrganizationId
                    && item.Disposition == CatalogDisposition.Incoming, cancellationToken);
                receivedToday = await db.Listings.AsNoTracking().CountAsync(item => item.OrganizationId == queue.OrganizationId
                    && item.Disposition == CatalogDisposition.Incoming && item.ReceivedAt >= todayStart, cancellationToken);
            }
            activeCount = await active.CountAsync(cancellationToken);
            waitingCount = await active.CountAsync(item => item.StageId == "pending_head", cancellationToken);
            overdueCount = await caseTasks.CountAsync(row => !row.Task.Completed && !row.Task.Deleted && row.Task.DueAt < (row.Task.DueHasTime ? now : todayStart), cancellationToken);
            deepCount = await active.CountAsync(item => db.CaseChecks.Any(check => check.PropertyCaseId == item.Id
                && check.Level == CaseCheckLevel.Deep), cancellationToken);
            acquiredCount = await visible.CountAsync(item => item.StageId == "acquired" && item.AcquiredAt >= now.AddDays(-30), cancellationToken);

            DateTimeOffset stalledBefore = now.AddDays(-3);
            var attentionCases = caseTasks.Where(row =>
                !row.Task.Completed && !row.Task.Deleted && row.Task.DueAt < (row.Task.DueHasTime ? now : todayStart)
                || row.Case.StageId == "returned"
                || !row.Task.Completed && !row.Task.Deleted && row.Task.DueAt == null && row.Task.RecordedAt < stalledBefore
                || row.HasCheckIssue
                || row.SourceChanged);
            procurementAttentionCount = await attentionCases.CountAsync(cancellationToken);
            var attentionRows = await attentionCases
                .OrderBy(row => row.Task.DueAt == null).ThenBy(row => row.Task.DueAt)
                .ThenByDescending(row => row.Case.StageId == "returned")
                .ThenBy(row => row.Case.BusinessNumber).Take(FeedSize).ToArrayAsync(cancellationToken);
            attentionItems.AddRange(attentionRows.Select(row => ProjectAttention(
                new(row.Case, row.Task, row.AssigneeId, row.HasCheckIssue, row.SourceChanged), now, todayStart)));

            var taskRows = await (from task in db.WorkTasks.AsNoTracking()
                                  join item in active on task.ObjectId equals item.Id
                                  where task.OrganizationId == queue.OrganizationId && task.ObjectType == "PropertyCase"
                                      && task.EmployeeId == identity.EmployeeId && !task.Completed && !task.Deleted
                                  orderby (task.DueAt < (task.DueHasTime ? now : todayStart)) descending, task.DueAt == null, task.DueAt, task.RecordedAt, task.Id
                                  select new { Item = item, Task = task }).Take(10).ToArrayAsync(cancellationToken);
            int taskTotal = await (from task in db.WorkTasks.AsNoTracking()
                                   join item in active on task.ObjectId equals item.Id
                                   where task.OrganizationId == queue.OrganizationId && task.ObjectType == "PropertyCase"
                                       && task.EmployeeId == identity.EmployeeId && !task.Completed && !task.Deleted
                                   select task.Id).CountAsync(cancellationToken);
            var notifications = await db.Notifications.AsNoTracking()
                .Where(item => item.OrganizationId == queue.OrganizationId && item.EmployeeId == identity.EmployeeId
                    && item.ObjectType == "PropertyCase" && item.ReadAt == null)
                .OrderByDescending(item => item.RecordedAt).Take(10).ToArrayAsync(cancellationToken);
            myWorkTotal = taskTotal + await db.Notifications.AsNoTracking().CountAsync(item => item.OrganizationId == queue.OrganizationId
                && item.EmployeeId == identity.EmployeeId && item.ObjectType == "PropertyCase" && item.ReadAt == null, cancellationToken);
            myWork = ProjectMyWork(taskRows.Select(row => (row.Item, row.Task)), notifications, now, todayStart, tomorrowStart)
                .Take(FeedSize).ToList();

            if (queue.Scope is AccessScope.Team or AccessScope.Department or AccessScope.Organization)
            {
                var workload = await (from item in active
                                      join assignment in db.WorkAssignments.AsNoTracking() on item.AssignmentId equals assignment.Id
                                      from task in db.WorkTasks.AsNoTracking()
                                .Where(t => t.OrganizationId == item.OrganizationId && t.ObjectType == "PropertyCase" && t.ObjectId == item.Id
                                    && ((!t.Completed && !t.Deleted) || t.Id == item.WorkTaskId))
                                .OrderBy(t => t.Completed || t.Deleted).ThenByDescending(t => t.DueAt < (t.DueHasTime ? now : todayStart))
                                .ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenBy(t => t.RecordedAt).ThenBy(t => t.Id).Take(1)
                                      group new { task } by assignment.EmployeeId into grouped
                                      select new
                                      {
                                          EmployeeId = grouped.Key,
                                          ActiveCases = grouped.Count(),
                                          OverdueCases = grouped.Count(value => !value.task.Completed && !value.task.Deleted && value.task.DueAt < (value.task.DueHasTime ? now : todayStart))
                                      })
                    .OrderByDescending(item => item.OverdueCases).ThenByDescending(item => item.ActiveCases).Take(8).ToArrayAsync(cancellationToken);
                teamTotal = await (from item in active
                                   join assignment in db.WorkAssignments.AsNoTracking() on item.AssignmentId equals assignment.Id
                                   select assignment.EmployeeId).Distinct().CountAsync(cancellationToken);
                Guid[] ids = workload.Select(item => item.EmployeeId).ToArray();
                Dictionary<Guid, string> names = await db.Employees.AsNoTracking()
                    .Where(item => item.OrganizationId == queue.OrganizationId && ids.Contains(item.Id))
                    .ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
                team = workload.Select(item => new OverviewTeamMember(item.EmployeeId,
                    names.GetValueOrDefault(item.EmployeeId, "Сотрудник"), item.ActiveCases, item.OverdueCases)).ToList();
            }

            market = await ReadMarketGroupsCoreAsync(db, effective,
                new("", MarketGroupSort.Name, 0, CompactMarketSize), now, cancellationToken);
        }

        OverviewCollectionSummary? collectionSummary = collection == null ? null
            : await ReadCollectionAsync(db, collection, now, todayStart, cancellationToken);
        if (collectionSummary != null)
        {
            foreach (OverviewCollectionStatus item in collectionSummary.Statuses.Where(item => item.Severity != OverviewSeverity.Info))
            {
                if (attentionItems.Count >= FeedSize) break;
                attentionItems.Add(new("collection:" + item.Label, item.Severity, item.Label, item.Value,
                    "Сейчас", "открыть управление", item.Url));
            }
        }

        int incomingAttention = queue == null || !effective.CanReadIncoming ? 0
            : await db.Listings.AsNoTracking().CountAsync(item => item.OrganizationId == queue.OrganizationId
                && item.Disposition == CatalogDisposition.Incoming && item.AttentionRequired, cancellationToken);
        if (queue != null && effective.CanReadIncoming && incomingAttention > 0 && attentionItems.Count < FeedSize)
            attentionItems.Add(new("incoming", OverviewSeverity.Info, $"{incomingAttention} новых предложений требуют разбора",
                "Входящие ещё не обработаны", Age(await db.Listings.AsNoTracking().Where(item => item.OrganizationId == queue.OrganizationId
                    && item.Disposition == CatalogDisposition.Incoming && item.AttentionRequired).MinAsync(item => (DateTimeOffset?)item.ReceivedAt, cancellationToken), now),
                "посмотреть входящие", "/incoming"));

        int collectionAttention = collectionSummary?.Statuses.Count(item => item.Severity != OverviewSeverity.Info) ?? 0;
        int attentionTotal = procurementAttentionCount + incomingAttention + collectionAttention;
        List<OverviewQuickAction> quick = [];
        if (canManage) quick.Add(new("add", "+ Добавить предложение", "/incoming?manual=true", true));
        if (effective.CanReadIncoming)
            quick.Add(new("incoming", "Открыть входящие", "/incoming", false));
        if (queue != null)
        {
            quick.Add(new("queue", "Очередь закупки", "/procurement", false));
            quick.Add(new("mine", "Мои объекты", "/procurement?mine=true", false));
        }

        int maxStage = Math.Max(1, Math.Max(newIncoming, Math.Max(activeCount, Math.Max(deepCount, Math.Max(waitingCount, acquiredCount)))));
        List<OverviewStageItem> stageItems = [];
        if (effective.CanReadIncoming)
            stageItems.Add(Stage("incoming", "Входящие", newIncoming, "новых предложений", maxStage, "/incoming"));
        if (queue != null)
        {
            stageItems.Add(Stage("work", "В работе", activeCount, overdueCount == 0 ? "объектов в работе" : $"{overdueCount} просрочено", maxStage, "/procurement"));
            stageItems.Add(Stage("deep", "Глубокая проверка", deepCount, "юридическая проверка", maxStage, "/procurement"));
            stageItems.Add(Stage("decision", "Решение", waitingCount, "ждут руководителя", maxStage, "/procurement?stage=pending_head"));
            stageItems.Add(Stage("acquired", "Куплено", acquiredCount, "за последние 30 дней", maxStage, "/procurement?stage=acquired"));
        }
        OverviewStageItem[] stages = stageItems.ToArray();

        return new(queue != null,
            new(newIncoming, receivedToday == 0 ? "нет новых сегодня" : $"{receivedToday} получено сегодня"),
            new(activeCount, deepCount == 0 ? "нет глубокой проверки" : $"{deepCount} на глубокой проверке"),
            new(attentionTotal, overdueCount == 0 ? "нет просроченных" : $"{overdueCount} просрочено"),
            new(waitingCount, waitingCount == 0 ? "решения не ожидаются" : "ожидают руководителя"),
            attentionItems.Take(FeedSize).ToArray(), attentionTotal, stages, myWork, myWorkTotal,
            collectionSummary, quick, team, teamTotal, market, now);
    }

    public async Task<MarketGroupPage> ReadMarketGroupsAsync(Subject subject, MarketGroupQuery query, CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await employeeAccess.ResolveAsync(subject, cancellationToken);
        if (!effective.CanReadProcurement && !effective.CanReadCollection)
            throw new AccessDeniedException();
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await ReadMarketGroupsCoreAsync(db, effective,
            query, time.GetUtcNow(), cancellationToken);
    }

    public async Task<SearchGroupMarketSettingsView> SaveMarketSettingsAsync(Subject subject,
        SaveSearchGroupMarketSettings command, string correlationId, CancellationToken cancellationToken)
    {
        Validate(command);
        EffectiveEmployeeAccess effective = await employeeAccess.ResolveAsync(subject, cancellationToken);
        if (!effective.CanManageCollection) throw new AccessDeniedException();
        AccessContext context = effective.OrganizationContext;
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        SearchGroup group = await db.SearchGroups.SingleOrDefaultAsync(item => item.Id == command.SearchGroupId
            && item.OrganizationId == context.OrganizationId && item.Active, cancellationToken) ?? throw new AccessDeniedException();
        SearchGroupMarketSettings? settings = await db.SearchGroupMarketSettings
            .SingleOrDefaultAsync(item => item.SearchGroupId == group.Id && item.OrganizationId == context.OrganizationId, cancellationToken);
        if (settings == null)
        {
            if (command.ExpectedVersion != 0) throw new DbUpdateConcurrencyException();
            settings = new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, SearchGroupId = group.Id
            };
            db.SearchGroupMarketSettings.Add(settings);
        }
        else if (settings.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException();

        settings.PeriodDays = command.PeriodDays;
        settings.AllowedPropertyTypes = command.AllowedPropertyTypes.Distinct().Select(item => item.ToString()).Order().ToArray();
        settings.MinPricePerSotka = command.MinPricePerSotka;
        settings.MaxPricePerSotka = command.MaxPricePerSotka;
        OrganizationWorkspace.AddAudit(db, context, subject, "SearchGroupMarketSettingsUpdated", "SearchGroup", group.Id,
            new { group.Name, settings.PeriodDays, settings.AllowedPropertyTypes, settings.MinPricePerSotka, settings.MaxPricePerSotka }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        return SettingsView(settings);
    }

    private static async Task<MarketGroupPage> ReadMarketGroupsCoreAsync(LandErpDbContext db, EffectiveEmployeeAccess effective,
        MarketGroupQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guid organizationId = effective.OrganizationId;
        int offset = Math.Max(0, query.Offset);
        int size = Math.Clamp(query.Size, 1, 50);
        string text = query.Text.Trim();
        if (text.Length > 200) throw new ArgumentException("Поиск по группам не должен превышать 200 символов.");
        IQueryable<SearchGroup> groupQuery = db.SearchGroups.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.Active);
        if (text.Length > 0) groupQuery = groupQuery.Where(item => EF.Functions.ILike(item.Name, $"%{text}%"));
        int total = await groupQuery.CountAsync(cancellationToken);
        bool pageBeforeAggregate = query.Sort == MarketGroupSort.Name;
        IQueryable<SearchGroup> selectedGroups = pageBeforeAggregate
            ? groupQuery.OrderBy(item => item.SortOrder).ThenBy(item => item.Name).Skip(offset).Take(size)
            : groupQuery.OrderBy(item => item.SortOrder).ThenBy(item => item.Name).Take(MarketSortCandidateLimit);
        GroupDb[] groups = await (from groupItem in selectedGroups
                                  join value in db.SearchGroupMarketSettings.AsNoTracking() on groupItem.Id equals value.SearchGroupId into values
                                  from settings in values.DefaultIfEmpty()
                                  select new GroupDb(groupItem.Id, groupItem.Name, groupItem.SortOrder, settings)).ToArrayAsync(cancellationToken);
        var aggregates = (await GroupMarketService.ReadCoreAsync(db, effective, groups.Select(item => item.Id).ToArray(), cancellationToken))
            .ToDictionary(item => item.SearchGroupId);
        IEnumerable<MarketGroupRow> rows = groups.Select(group =>
        {
            GroupMarketView aggregate = aggregates[group.Id];
            return new MarketGroupRow(group.Id, group.Name, group.SortOrder, aggregate.MedianPricePerSotka, aggregate.AveragePricePerSotka,
                aggregate.ParticipantCount, 0, 0,
                group.Settings == null ? DefaultSettings() : SettingsView(group.Settings), false, aggregate.CanReadParticipants, aggregate);
        });
        rows = query.Sort switch
        {
            MarketGroupSort.MedianDescending => rows.OrderByDescending(item => item.MedianPricePerSotka.HasValue)
                .ThenByDescending(item => item.MedianPricePerSotka).ThenBy(item => item.Name),
            MarketGroupSort.MedianAscending => rows.OrderByDescending(item => item.MedianPricePerSotka.HasValue)
                .ThenBy(item => item.MedianPricePerSotka).ThenBy(item => item.Name),
            MarketGroupSort.SampleDescending => rows.OrderByDescending(item => item.IncludedCount).ThenBy(item => item.Name),
            _ => rows.OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
        };
        MarketGroupRow[] page = pageBeforeAggregate ? rows.ToArray() : rows.Skip(offset).Take(size).ToArray();
        int boundedTotal = pageBeforeAggregate ? total : Math.Min(total, MarketSortCandidateLimit);
        return new(page, boundedTotal, offset, size);
    }

    private static async Task<OverviewCollectionSummary> ReadCollectionAsync(LandErpDbContext db, AccessContext context,
        DateTimeOffset now, DateTimeOffset todayStart, CancellationToken cancellationToken)
    {
        DateTimeOffset onlineSince = now.AddMinutes(-3);
        DateTimeOffset attentionSince = now.AddDays(-1);
        int enabled = await db.CollectorAgents.AsNoTracking().CountAsync(item => item.OrganizationId == context.OrganizationId && item.Enabled, cancellationToken);
        int online = await db.CollectorAgents.AsNoTracking().CountAsync(item => item.OrganizationId == context.OrganizationId
            && item.Enabled && item.LastHeartbeatAt >= onlineSince, cancellationToken);
        int searches = await db.SearchConfigurations.AsNoTracking().CountAsync(item => item.OrganizationId == context.OrganizationId && item.Enabled, cancellationToken);
        int processed = await db.CollectionJobs.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId
            && item.CompletedAt >= todayStart).SumAsync(item => (int?)item.ProcessedCount, cancellationToken) ?? 0;
        DateTimeOffset? lastSuccess = await db.CollectionJobs.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId
            && (item.State == CollectionJobState.Completed || item.State == CollectionJobState.LimitReached))
            .MaxAsync(item => item.CompletedAt, cancellationToken);
        DateTimeOffset? lastNew = await db.Listings.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId)
            .MaxAsync(item => (DateTimeOffset?)item.ReceivedAt, cancellationToken);
        int attention = await db.CollectionJobs.AsNoTracking().CountAsync(item => item.OrganizationId == context.OrganizationId
            && item.CreatedAt >= attentionSince && item.RequiresOperatorAttention, cancellationToken);
        List<OverviewCollectionStatus> statuses =
        [
            new(OverviewSeverity.Info, "Последний успешный сбор", lastSuccess == null ? "ещё не выполнялся" : RelativeTime(lastSuccess, now), "/collectors")
        ];
        if (attention > 0) statuses.Add(new(OverviewSeverity.Warning, "Сбор данных требует внимания", $"{attention} заданий за последние сутки", "/collectors"));
        else if (enabled > 0 && online == 0) statuses.Add(new(OverviewSeverity.Critical, "Нет связи со сборщиками", "проверьте локальные приложения", "/collectors"));
        else statuses.Add(new(OverviewSeverity.Info, "Сбор данных", "ручное вмешательство не требуется", "/collectors"));
        statuses.Add(new(OverviewSeverity.Info, "Последнее новое предложение", lastNew == null ? "данных пока нет" : RelativeTime(lastNew, now), "/incoming"));
        return new(online, enabled, searches, processed, statuses, "/collectors");
    }

    private static OverviewAttentionItem ProjectAttention(CaseTaskRow row, DateTimeOffset now, DateTimeOffset todayStart)
    {
        if (!row.Task.Completed && !row.Task.Deleted && row.Task.DueAt < (row.Task.DueHasTime ? now : todayStart))
            return new(row.Case.Id.ToString(), OverviewSeverity.Critical, CaseTitle(row.Case), row.Task.Title,
                Age(row.Task.DueAt, now), "выполнить следующий шаг", $"/procurement/{row.Case.Id}");
        if (row.Case.StageId == "returned")
            return new(row.Case.Id.ToString(), OverviewSeverity.Warning, CaseTitle(row.Case), "Руководитель вернул объект на доработку",
                Age(row.Case.RecordedAt, now), row.Task.Title, $"/procurement/{row.Case.Id}");
        if (row.HasCheckIssue)
            return new(row.Case.Id.ToString(), OverviewSeverity.Warning, CaseTitle(row.Case), "В проверках есть вопросы или блокирующее замечание",
                Age(row.Task.RecordedAt, now), "открыть проверки", $"/procurement/{row.Case.Id}");
        if (row.SourceChanged)
            return new(row.Case.Id.ToString(), OverviewSeverity.Info, CaseTitle(row.Case), "Источник объекта изменился",
                Age(row.Task.RecordedAt, now), "сверить данные", $"/procurement/{row.Case.Id}");
        return new(row.Case.Id.ToString(), OverviewSeverity.Warning, CaseTitle(row.Case), "Нет назначенного срока следующего действия",
            Age(row.Task.RecordedAt, now), "назначить следующий шаг", $"/procurement/{row.Case.Id}");
    }

    private static IEnumerable<OverviewWorkItem> ProjectMyWork(IEnumerable<(PropertyCase Item, WorkTask Task)> tasks,
        IEnumerable<InternalNotification> notifications, DateTimeOffset now, DateTimeOffset todayStart, DateTimeOffset tomorrowStart)
    {
        List<(DateTimeOffset Sort, OverviewWorkItem Item)> result = [];
        foreach ((PropertyCase item, WorkTask task) in tasks)
        {
            OverviewDueState state = task.DueAt switch
            {
                null => OverviewDueState.None,
                DateTimeOffset value when value < (task.DueHasTime ? now : todayStart) => OverviewDueState.Overdue,
                DateTimeOffset value when value < tomorrowStart => OverviewDueState.Today,
                _ => OverviewDueState.Upcoming
            };
            result.Add((task.DueAt ?? DateTimeOffset.MaxValue, new(task.Id.ToString(), CaseTitle(item), StageLabel(item.StageId),
                task.Title, state == OverviewDueState.Overdue ? "Срок действия прошёл" : "Следующее действие по объекту",
                WorkTaskDeadline.Format(task.DueAt, task.DueHasTime), state, $"/procurement/{item.Id}")));
        }
        foreach (InternalNotification notification in notifications)
            result.Add((notification.RecordedAt, new(notification.Id.ToString(), notification.Title, "Новое уведомление",
                "Открыть объект", "Назначение или решение требует вашего внимания", RelativeTime(notification.RecordedAt, now),
                OverviewDueState.Today, $"/procurement/{notification.ObjectId}")));
        return result.OrderBy(item => item.Item.DueState == OverviewDueState.Overdue ? 0 : item.Item.DueState == OverviewDueState.Today ? 1 : 2)
            .ThenBy(item => item.Sort).Select(item => item.Item);
    }

    private static OverviewStageItem Stage(string key, string label, int count, string caption, int max, string url) =>
        new(key, label, count, caption, count == 0 ? 0 : Math.Max(8, (int)Math.Round(count * 100d / max)), url);

    private static SearchGroupMarketSettingsView DefaultSettings() => new(30, DefaultTypes.ToArray(), null, null, 0);
    private static SearchGroupMarketSettingsView SettingsView(SearchGroupMarketSettings settings) => new(settings.PeriodDays,
        settings.AllowedPropertyTypes.Select(value => Enum.TryParse(value, out IncomingLandType parsed) ? parsed : IncomingLandType.Other)
            .Distinct().ToArray(), settings.MinPricePerSotka, settings.MaxPricePerSotka, settings.Version);

    private static void Validate(SaveSearchGroupMarketSettings command)
    {
        if (!AllowedPeriods.Contains(command.PeriodDays)) throw new ArgumentException("Выберите период 7, 30, 90 или 180 дней.");
        if (command.AllowedPropertyTypes.Length == 0 || command.AllowedPropertyTypes.Any(item => !Enum.IsDefined(item)))
            throw new ArgumentException("Выберите хотя бы один поддерживаемый тип участка.");
        if (command.MinPricePerSotka < 0 || command.MaxPricePerSotka < 0
            || command.MinPricePerSotka != null && command.MaxPricePerSotka != null && command.MinPricePerSotka > command.MaxPricePerSotka)
            throw new ArgumentException("Проверьте диапазон цены за сотку.");
    }

    private static (DateTimeOffset Start, DateTimeOffset End) TodayBounds(DateTimeOffset now)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(DataConventions.BusinessTimeZoneId);
        DateTimeOffset local = TimeZoneInfo.ConvertTime(now, zone);
        DateTime start = new(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return (new(TimeZoneInfo.ConvertTimeToUtc(start, zone)), new(TimeZoneInfo.ConvertTimeToUtc(start.AddDays(1), zone)));
    }

    private static string CaseTitle(PropertyCase item) => string.IsNullOrWhiteSpace(item.BusinessNumber)
        ? item.WorkingTitle : $"{item.WorkingTitle} · {item.BusinessNumber}";
    private static string StageLabel(string stage) => stage switch
    {
        "analysis" => "Первичная проверка", "clarify" => "Уточнение", "monitor" => "Наблюдение",
        "pending_head" => "Решение руководителя", "returned" => "Возвращён на доработку", "approved" => "Одобрен",
        "negotiation" => "Переговоры и проверки", "acquired" => "Куплено", _ => "В работе"
    };
    private static string Age(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value == null) return "Сейчас";
        TimeSpan age = now - value.Value;
        if (age < TimeSpan.Zero) return "Сегодня";
        if (age.TotalMinutes < 60) return $"{Math.Max(1, (int)age.TotalMinutes)} мин";
        if (age.TotalHours < 24) return $"{Math.Max(1, (int)age.TotalHours)} ч";
        return $"{Math.Max(1, (int)age.TotalDays)} дн";
    }
    private static string RelativeTime(DateTimeOffset? value, DateTimeOffset now) => value == null ? "—" : Age(value, now) + " назад";
    private static string Deadline(DateTimeOffset? due, OverviewDueState state) => state switch
    {
        OverviewDueState.Overdue => "Просрочено",
        OverviewDueState.Today => due == null ? "Сегодня" : $"Сегодня · {due.Value:HH:mm}",
        OverviewDueState.Upcoming => due?.ToString("dd.MM · HH:mm", CultureInfo.GetCultureInfo("ru-RU")) ?? "—",
        _ => "Срок не назначен"
    };
}
