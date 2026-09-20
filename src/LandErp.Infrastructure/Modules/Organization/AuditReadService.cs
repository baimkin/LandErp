using System.Globalization;
using System.Text;
using System.Text.Json;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Organization;

public sealed class AuditReadService(IDbContextFactory<LandErpDbContext> factory, IAccessControl access) : IAuditReadService
{
    private const int MaximumExportRows = 10_000;
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };
    private static readonly string[] SecurityActions =
    [
        "LoginSucceeded", "TemporaryPasswordLogin", "TemporaryPasswordChanged", "MfaEnabled",
        "EmployeePasswordReset", "EmployeeActivated", "OwnerBootstrapped", "CollectorCredentialRotated"
    ];
    private static readonly string[] DataChangeActions =
    [
        "DepartmentCreated", "DepartmentUpdated", "DepartmentArchived", "DepartmentRestored",
        "TeamCreated", "TeamUpdated", "TeamArchived", "TeamRestored", "PositionCreated", "PositionUpdated",
        "PositionArchived", "PositionRestored", "EmployeeCreated", "EmployeeInvited", "EmployeeDeactivated",
        "EmployeeRestored", "EmployeeWorkTransferred", "EmployeeWorkHandoverPending", "AssignmentChanged", "CollectorAgentCreated", "CollectorAgentRevoked",
        "CollectionSearchCreated", "CollectionSearchUpdated", "CollectionSearchGroupCreated", "CollectionSearchGroupArchived",
        "CatalogItemCreatedManually", "CatalogDispositionChanged", "CatalogMonitoringStarted", "CatalogItemTakenToWork",
        "CatalogItemLinkedToCase", "CatalogDuplicateConfirmed", "CatalogDuplicateRejected", "CatalogDuplicateSettingsChanged",
        "PropertyCaseResumedFromCatalog", "SellerContactRecorded", "CaseNoteAdded",
        "CaseNegotiationAdded", "CaseCheckSaved", "CaseCheckTemplateSaved", "InspectionTemplateSaved",
        "SiteInspectionStarted", "SiteInspectionDraftSaved", "SiteInspectionCompleted", "PropertyCaseAcquired",
        "CaseAttachmentAdded", "CaseAttachmentUploadRetried", "CaseFactAppliedFromSource"
    ];

    public async Task<AuditPage> ReadAsync(Subject subject, AuditQuery query, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        string zoneId = await db.Organizations.Where(item => item.Id == context.OrganizationId)
            .Select(item => item.BusinessTimeZone).SingleAsync(cancellationToken);
        QueryWindow window = Window(query, zoneId);

        IQueryable<AuditEvent> period = db.AuditEvents.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId
                && item.RecordedAt >= window.FromUtc && item.RecordedAt < window.ToExclusiveUtc);
        AuditStatistics statistics = new(
            await period.CountAsync(cancellationToken),
            await period.Select(item => item.ActorId).Distinct().CountAsync(cancellationToken),
            await period.CountAsync(item => DataChangeActions.Contains(item.Action) || item.Action.StartsWith("Procurement"), cancellationToken),
            await period.CountAsync(item => SecurityActions.Contains(item.Action), cancellationToken));

        IQueryable<AuditEvent> candidates = period;
        if (window.Search.Length > 0)
        {
            candidates = db.AuditEvents.FromSqlInterpolated($@"
                SELECT audit.* FROM foundation.audit_events AS audit
                WHERE audit.organization_id = {context.OrganizationId}
                  AND strpos(lower(audit.action), lower({window.Search})) > 0
                   OR audit.organization_id = {context.OrganizationId}
                  AND strpos(lower(audit.object_type), lower({window.Search})) > 0
                   OR audit.organization_id = {context.OrganizationId}
                  AND strpos(lower(audit.changes::text), lower({window.Search})) > 0
                   OR audit.organization_id = {context.OrganizationId}
                  AND EXISTS (
                      SELECT 1 FROM organization.employees AS employee
                      WHERE employee.organization_id = {context.OrganizationId}
                        AND employee.user_id = audit.actor_id
                        AND strpos(lower(employee.display_name), lower({window.Search})) > 0)")
                .AsNoTracking().Where(item => item.RecordedAt >= window.FromUtc && item.RecordedAt < window.ToExclusiveUtc);
        }
        IQueryable<AuditEvent> filtered = ApplyModuleAndCategory(candidates, window.Module, query.Category);
        if (window.ActorId != null) filtered = filtered.Where(item => item.ActorId == window.ActorId);

        int total = await filtered.CountAsync(cancellationToken);
        AuditEvent[] rows = await filtered.OrderByDescending(item => item.RecordedAt).ThenByDescending(item => item.Id)
            .Skip((window.Page - 1) * window.PageSize).Take(window.PageSize).ToArrayAsync(cancellationToken);
        ProjectionContext projection = await ProjectionContext.CreateAsync(db, context.OrganizationId, rows, cancellationToken);
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        AuditEventView[] items = rows.Select(item => Project(item, zone, projection)).ToArray();
        AuditActorOption[] actors = await (from employee in db.Employees.AsNoTracking()
            where employee.OrganizationId == context.OrganizationId
            orderby employee.DisplayName
            select new AuditActorOption(employee.UserId, employee.DisplayName, !employee.Active)).ToArrayAsync(cancellationToken);

        return new(items, actors,
        [
            new("organization", "Организация"), new("security", "Безопасность"),
            new("collection", "Сбор данных"), new("procurement", "Закупка"), new("system", "Система")
        ], statistics, window.Page, window.PageSize, total, zoneId, window.From, window.To);
    }

    public async Task<AuditTechnicalDetails> ReadTechnicalAsync(Subject subject, Guid eventId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        AuditEvent item = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == eventId && value.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        return new(item.Id, item.Action, item.ObjectType, item.ObjectId, item.ActorId, item.CorrelationId,
            PrettyJson(item.Changes));
    }

    public async Task<AuditExport> ExportCsvAsync(Subject subject, AuditQuery query, CancellationToken cancellationToken)
    {
        AuditPage first = await ReadAsync(subject, query with { Page = 1, PageSize = 100 }, cancellationToken);
        if (first.TotalCount > MaximumExportRows)
            throw new ArgumentException("Для экспорта выберите более короткий период или дополнительные фильтры.");
        List<AuditEventView> rows = [.. first.Items];
        for (int page = 2; rows.Count < first.TotalCount; page++)
        {
            AuditPage next = await ReadAsync(subject, query with { Page = page, PageSize = 100 }, cancellationToken);
            rows.AddRange(next.Items);
        }

        StringBuilder csv = new("\uFEFFДата и время;Раздел;Действие;Описание;Автор;Объект;Изменения\r\n");
        foreach (AuditEventView row in rows)
        {
            string changes = string.Join(" | ", row.Changes.Select(item => $"{item.Field}: {item.Before} → {item.After}"));
            csv.AppendLine(string.Join(';', new[]
            {
                row.DisplayTime.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.GetCultureInfo("ru-RU")), row.Module,
                row.Title, row.Summary, row.ActorDisplayName, $"{row.ObjectDisplayType}: {row.ObjectDisplayName}", changes
            }.Select(Csv)));
        }
        return new(Encoding.UTF8.GetBytes(csv.ToString()), $"landerp-audit-{first.From:yyyyMMdd}-{first.To:yyyyMMdd}.csv");
    }

    private async Task<AccessContext> RequireAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.AuditRead, cancellationToken);
        return context.Scope == AccessScope.Organization ? context : throw new AccessDeniedException();
    }

    private static QueryWindow Window(AuditQuery query, string zoneId)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        DateOnly from = query.From ?? today.AddDays(-6);
        DateOnly to = query.To ?? today;
        if (to < from || to.DayNumber - from.DayNumber > 366) throw new ArgumentException("Период аудита должен быть от 1 до 367 дней.");
        int pageSize = Math.Clamp(query.PageSize, 10, 100);
        int page = Math.Max(1, query.Page);
        DateTimeOffset fromUtc = ToUtc(from, zone);
        DateTimeOffset toExclusiveUtc = ToUtc(to.AddDays(1), zone);
        return new(from, to, fromUtc, toExclusiveUtc, query.ActorId,
            NormalizeModule(query.Module), query.Search?.Trim() ?? "", page, pageSize);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeZoneInfo zone)
    {
        DateTime local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static IQueryable<AuditEvent> ApplyModuleAndCategory(IQueryable<AuditEvent> query, string module, AuditCategory category)
    {
        if (module == "security") query = query.Where(item => SecurityActions.Contains(item.Action));
        else if (module == "organization") query = query.Where(item => item.Action.StartsWith("Department") || item.Action.StartsWith("Team")
            || item.Action.StartsWith("Position") || item.Action.StartsWith("Employee") || item.Action == "AssignmentChanged" || item.Action == "OwnerBootstrapped");
        else if (module == "collection") query = query.Where(item => item.Action.StartsWith("Collection") || item.Action.StartsWith("Collector"));
        else if (module == "procurement") query = query.Where(item => item.Action.StartsWith("Catalog") || item.Action.StartsWith("Procurement")
            || item.Action.StartsWith("PropertyCase") || item.Action.StartsWith("Case") || item.Action.StartsWith("Seller")
            || item.Action.StartsWith("SiteInspection") || item.Action.StartsWith("Inspection"));
        else if (module == "system") query = query.Where(item => !SecurityActions.Contains(item.Action)
            && !item.Action.StartsWith("Department") && !item.Action.StartsWith("Team") && !item.Action.StartsWith("Position")
            && !item.Action.StartsWith("Employee") && item.Action != "AssignmentChanged" && item.Action != "OwnerBootstrapped"
            && !item.Action.StartsWith("Collection") && !item.Action.StartsWith("Collector") && !item.Action.StartsWith("Catalog")
            && !item.Action.StartsWith("Procurement") && !item.Action.StartsWith("PropertyCase") && !item.Action.StartsWith("Case")
            && !item.Action.StartsWith("Seller") && !item.Action.StartsWith("SiteInspection") && !item.Action.StartsWith("Inspection"));

        if (category == AuditCategory.Security) query = query.Where(item => SecurityActions.Contains(item.Action));
        else if (category == AuditCategory.Collection) query = query.Where(item => item.Action.StartsWith("Collection") || item.Action.StartsWith("Collector"));
        else if (category == AuditCategory.Procurement) query = query.Where(item => item.Action.StartsWith("Catalog") || item.Action.StartsWith("Procurement")
            || item.Action.StartsWith("PropertyCase") || item.Action.StartsWith("Case") || item.Action.StartsWith("Seller")
            || item.Action.StartsWith("SiteInspection") || item.Action.StartsWith("Inspection"));
        else if (category == AuditCategory.DataChanges) query = query.Where(item => DataChangeActions.Contains(item.Action) || item.Action.StartsWith("Procurement"));
        return query;
    }

    private static AuditEventView Project(AuditEvent item, TimeZoneInfo zone, ProjectionContext context)
    {
        ObjectSnapshot target = context.Object(item.ObjectType, item.ObjectId, item.Changes);
        ActorSnapshot actor = context.Actor(item.ActorId);
        Descriptor descriptor = Descriptor.For(item.Action, target);
        DateTimeOffset displayTime = TimeZoneInfo.ConvertTime(item.RecordedAt, zone);
        IReadOnlyList<AuditChangeView> changes = SemanticChanges(item.Action, item.Changes, context);
        return new(item.Id, item.RecordedAt, displayTime, DateOnly.FromDateTime(displayTime.DateTime), descriptor.Module,
            descriptor.Category, descriptor.Tone, descriptor.Icon, descriptor.Title, descriptor.Summary,
            actor.Name, actor.Context, target.Type, target.Name, target.Archived, changes);
    }

    private static List<AuditChangeView> SemanticChanges(string action, string json, ProjectionContext context)
    {
        if (action is "LoginSucceeded" or "TemporaryPasswordLogin") return [new("Результат", "—", "Успешный вход")];
        if (action == "CatalogItemViewed") return [];
        if (action == "TemporaryPasswordChanged") return [new("Результат", "—", "Временный пароль заменён")];
        if (action == "MfaEnabled") return [new("Результат", "—", "Двухфакторная защита включена")];
        if (action == "EmployeePasswordReset") return [new("Результат", "—", "Выдан новый временный пароль")];
        if (action == "CollectionJobQueued") return [new("Состояние задания", "—", "Ожидает выполнения")];
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return [new("Результат", "—", "Действие выполнено")];
            JsonElement before = root.TryGetProperty("Before", out JsonElement beforeValue) ? beforeValue : default;
            JsonElement after = root.TryGetProperty("After", out JsonElement afterValue) ? afterValue : default;
            List<AuditChangeView> result = [];
            if (after.ValueKind == JsonValueKind.Object)
            {
                Dictionary<string, JsonElement> previous = before.ValueKind == JsonValueKind.Object
                    ? before.EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.OrdinalIgnoreCase) : [];
                foreach (JsonProperty property in after.EnumerateObject())
                {
                    if (!Useful(property.Name)) continue;
                    string oldValue = previous.TryGetValue(property.Name, out JsonElement old) ? DisplayValue(property.Name, old, context) : "—";
                    string newValue = DisplayValue(property.Name, property.Value, context);
                    if (!string.Equals(oldValue, newValue, StringComparison.Ordinal)) result.Add(new(FieldLabel(property.Name), oldValue, newValue));
                }
            }
            else
            {
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (!Useful(property.Name) || property.Name is "Before" or "After") continue;
                    result.Add(new(FieldLabel(property.Name), "—", DisplayValue(property.Name, property.Value, context)));
                }
            }
            return result.Count == 0 ? [new("Результат", "—", "Действие выполнено")] : result;
        }
        catch (JsonException)
        {
            return [new("Результат", "—", "Действие зафиксировано")];
        }
    }

    private static string DisplayValue(string field, JsonElement value, ProjectionContext context)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "Не указано";
        if (value.ValueKind == JsonValueKind.True) return "Да";
        if (value.ValueKind == JsonValueKind.False) return "Нет";
        if (value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString() ?? "";
            if (Guid.TryParse(text, out Guid id)) return context.Reference(id);
            return string.IsNullOrWhiteSpace(text) ? "Не указано" : text;
        }
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (field.Equals("Scope", StringComparison.OrdinalIgnoreCase) && value.TryGetInt32(out int scope))
                return scope switch { 0 => "Свои", 1 => "Назначенные объекты", 2 => "Команда", 3 => "Отдел", 4 => "Вся организация", _ => "Изменено" };
            return value.GetRawText();
        }
        if (value.ValueKind == JsonValueKind.Array) return value.GetArrayLength() == 0 ? "Нет" : $"Элементов: {value.GetArrayLength()}";
        return "Обновлено";
    }

    // Replay metadata is available only in authorized technical details, not business changes or CSV.
    private static bool Useful(string name) => !name.Equals("CommandReplay", StringComparison.OrdinalIgnoreCase)
        && !name.Contains("Password", StringComparison.OrdinalIgnoreCase)
        && !name.Contains("Token", StringComparison.OrdinalIgnoreCase) && !name.Contains("Credential", StringComparison.OrdinalIgnoreCase)
        && !name.Contains("Secret", StringComparison.OrdinalIgnoreCase) && !name.Contains("Cookie", StringComparison.OrdinalIgnoreCase)
        && name is not ("ExpectedVersion" or "Version" or "DataRevision" or "PreviousRevision");

    private static string FieldLabel(string value) => value switch
    {
        "Name" or "Label" => "Название", "DisplayName" => "ФИО", "Login" => "Логин", "Description" => "Описание",
        "DepartmentId" or "OrgUnitId" => "Отдел", "TeamId" => "Команда", "PositionId" => "Должность",
        "ManagerId" or "ManagerEmployeeId" => "Руководитель", "RecipientEmployeeId" => "Получатель", "Role" or "RoleId" => "Роль", "Scope" => "Область доступа",
        "Active" or "Enabled" => "Состояние", "Source" => "Источник", "MaxPages" => "Предел страниц",
        "ScheduleKind" => "Расписание", "State" => "Состояние", "Title" or "WorkingTitle" => "Название объекта",
        "Price" or "WorkingPrice" or "AcquisitionPrice" => "Цена", "Location" or "WorkingLocation" => "Расположение",
        "Reason" => "Причина", "Comment" => "Комментарий", "Result" => "Результат", "MustChangePassword" => "Смена пароля при входе",
        "CandidateThreshold" => "Общий порог дубля", "DescriptionSimilarityPercent" => "Похожесть описания, %",
        "AreaTolerancePercent" => "Допуск площади, %", "PhotoHammingDistance" => "Чувствительность фото",
        "StrongPhotoMatches" => "Сильное совпадение, фото", "CommonPhotoMaxListings" => "Порог типовой картинки",
        _ => SplitWords(value)
    };

    private static string SplitWords(string value)
    {
        StringBuilder result = new();
        foreach (char character in value)
        {
            if (result.Length > 0 && char.IsUpper(character)) result.Append(' ');
            result.Append(character);
        }
        return result.ToString();
    }

    private static string NormalizeModule(string? module) => module?.Trim().ToLowerInvariant() is "organization" or "security" or "collection" or "procurement" or "system" ? module.Trim().ToLowerInvariant() : "";
    private static string PrettyJson(string value) { try { using JsonDocument document = JsonDocument.Parse(value); return JsonSerializer.Serialize(document.RootElement, PrettyJsonOptions); } catch (JsonException) { return "{\n  \"status\": \"Недоступный формат старого события\"\n}"; } }
    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal) + '"';

    private sealed record QueryWindow(DateOnly From, DateOnly To, DateTimeOffset FromUtc, DateTimeOffset ToExclusiveUtc,
        Guid? ActorId, string Module, string Search, int Page, int PageSize);

    private sealed record Descriptor(string Module, string Category, string Tone, string Icon, string Title, string Summary)
    {
        public static Descriptor For(string action, ObjectSnapshot target)
        {
            string summary = target.Name;
            return action switch
            {
                "LoginSucceeded" => new("Безопасность", "Входы и доступ", "success", "✓", "Вход в систему", "Успешная авторизация"),
                "TemporaryPasswordLogin" => new("Безопасность", "Входы и доступ", "warning", "!", "Вход с временным паролем", "Требуется смена пароля"),
                "TemporaryPasswordChanged" => new("Безопасность", "Входы и доступ", "success", "✓", "Временный пароль изменён", summary),
                "MfaEnabled" => new("Безопасность", "Входы и доступ", "success", "✓", "Включена двухфакторная защита", summary),
                "EmployeePasswordReset" => new("Безопасность", "Входы и доступ", "warning", "↻", "Сброшен пароль сотрудника", summary),
                "EmployeeActivated" => new("Организация", "Изменения данных", "success", "✓", "Сотрудник активировал учётную запись", summary),
                "OwnerBootstrapped" => new("Безопасность", "Входы и доступ", "success", "+", "Создана учётная запись владельца", summary),
                "DepartmentCreated" => Data("Создан отдел", summary), "DepartmentUpdated" => Data("Изменён отдел", summary),
                "DepartmentArchived" => Data("Отдел архивирован", summary, "warning"), "DepartmentRestored" => Data("Отдел восстановлен", summary, "success"),
                "TeamCreated" => Data("Создана команда", summary), "TeamUpdated" => Data("Изменена команда", summary),
                "TeamArchived" => Data("Команда архивирована", summary, "warning"), "TeamRestored" => Data("Команда восстановлена", summary, "success"),
                "PositionCreated" => Data("Создана должность", summary), "PositionUpdated" => Data("Изменена должность", summary),
                "PositionArchived" => Data("Должность архивирована", summary, "warning"), "PositionRestored" => Data("Должность восстановлена", summary, "success"),
                "EmployeeCreated" => Data("Создан сотрудник", summary), "EmployeeInvited" => Data("Приглашён сотрудник", summary),
                "EmployeeDeactivated" => Data("Сотрудник отключён", summary, "warning"), "EmployeeRestored" => Data("Сотрудник восстановлен", summary, "success"),
                "EmployeeWorkTransferred" => Data("Передана активная работа сотрудника", summary),
                "EmployeeWorkHandoverPending" => Data("Требуется переназначение после отзыва доступа", summary, "warning"),
                "AssignmentChanged" => Data("Изменены назначение и доступ сотрудника", summary),
                "CollectorAgentCreated" => Collection("Подключён парсер", summary), "CollectorAgentRevoked" => Collection("Парсер отключён", summary, "warning"),
                "CollectorCredentialRotated" => new("Безопасность", "Входы и доступ", "warning", "↻", "Обновлён доступ парсера", summary),
                "CollectionSearchCreated" => Collection("Создан поисковый запрос", summary), "CollectionSearchUpdated" => Collection("Изменён поисковый запрос", summary),
                "CollectionSearchGroupCreated" => Collection("Создана группа поисков", summary), "CollectionSearchGroupArchived" => Collection("Группа поисков архивирована", summary, "warning"),
                "CollectionJobQueued" => Collection("Поставлено задание на сбор данных", summary),
                "CatalogItemCreatedManually" => Procurement("Добавлено входящее предложение", summary),
                "CatalogItemViewed" => Procurement("Просмотрено входящее предложение", summary),
                "CatalogDispositionChanged" => Procurement("Изменено состояние предложения", summary),
                "CatalogMonitoringStarted" => Procurement("Предложение поставлено на мониторинг", summary),
                "CatalogItemTakenToWork" => Procurement("Предложение взято в работу", summary, "success"),
                "CatalogItemLinkedToCase" => Procurement("Источник связан с объектом закупки", summary),
                "CatalogDuplicateConfirmed" => Procurement("Подтверждён дубль входящего предложения", summary, "warning"),
                "CatalogDuplicateRejected" => Procurement("Отклонён кандидат на дубль", summary),
                "CatalogDuplicateSettingsChanged" => Procurement("Изменены настройки определения дублей", summary),
                "PropertyCaseResumedFromCatalog" => Procurement("Работа по объекту возобновлена", summary, "success"),
                "SellerContactRecorded" => Procurement("Зафиксирован контакт с продавцом", summary), "CaseNoteAdded" => Procurement("Добавлена заметка", summary),
                "CaseNegotiationAdded" => Procurement("Добавлены переговоры", summary), "CaseCheckSaved" => Procurement("Сохранена проверка", summary),
                "CaseCheckTemplateSaved" => Procurement("Изменён шаблон проверки", summary), "InspectionTemplateSaved" => Procurement("Изменён шаблон осмотра", summary),
                "SiteInspectionStarted" => Procurement("Начат осмотр объекта", summary), "SiteInspectionDraftSaved" => Procurement("Сохранён черновик осмотра", summary),
                "SiteInspectionCompleted" => Procurement("Осмотр объекта завершён", summary, "success"), "PropertyCaseAcquired" => Procurement("Покупка объекта подтверждена", summary, "success"),
                "CaseAttachmentAdded" => Procurement("Добавлено вложение", summary), "CaseAttachmentUploadRetried" => Procurement("Повторена загрузка вложения", summary),
                "CaseFactAppliedFromSource" => Procurement("Данные источника приняты в карточку", summary),
                _ when action.StartsWith("Procurement", StringComparison.Ordinal) => Procurement(ProcurementTitle(action), summary),
                _ => new("Система", "Прочее", "neutral", "•", "Зафиксировано системное действие", "Событие сохранено и доступно для диагностики")
            };
        }

        private static Descriptor Data(string title, string summary, string tone = "info") => new("Организация", "Изменения данных", tone, tone == "warning" ? "!" : "+", title, summary);
        private static Descriptor Collection(string title, string summary, string tone = "info") => new("Сбор данных", "Сбор данных", tone, "↕", title, summary);
        private static Descriptor Procurement(string title, string summary, string tone = "info") => new("Закупка", "Закупка", tone, "▣", title, summary);
        private static string ProcurementTitle(string action) => action[11..] switch { "Monitor" => "Объект поставлен на мониторинг", "Clarify" => "Запрошено уточнение", "Reject" => "Объект отклонён", "Forward" => "Объект передан руководителю", "Return" => "Объект возвращён на доработку", "Approve" => "Решение по объекту одобрено", _ => "Изменён объект закупки" };
    }

    private sealed record ObjectSnapshot(string Type, string Name, bool Archived);
    private sealed record ActorSnapshot(string Name, string Context);

    private sealed class ProjectionContext(Dictionary<string, ObjectSnapshot> objects, Dictionary<Guid, ActorSnapshot> actors,
        Dictionary<Guid, string> references)
    {
        public ActorSnapshot Actor(Guid id) => actors.GetValueOrDefault(id) ?? new("Система", "Автоматическое действие");
        public string Reference(Guid id) => references.GetValueOrDefault(id, "Связанный объект");
        public ObjectSnapshot Object(string type, Guid id, string json)
        {
            if (objects.TryGetValue(Key(type, id), out ObjectSnapshot? value)) return value;
            string snapshot = SnapshotName(json);
            return new(TypeLabel(type), snapshot.Length == 0 ? "Архивный или недоступный объект" : snapshot, true);
        }

        public static async Task<ProjectionContext> CreateAsync(LandErpDbContext db, Guid organizationId, AuditEvent[] rows, CancellationToken cancellationToken)
        {
            Dictionary<string, ObjectSnapshot> objects = [];
            Dictionary<Guid, string> references = [];
            var actorRows = await (from employee in db.Employees.AsNoTracking()
                join user in db.Users.AsNoTracking() on employee.UserId equals user.Id
                where employee.OrganizationId == organizationId
                select new { employee.UserId, employee.DisplayName, employee.Active, user.UserName }).ToArrayAsync(cancellationToken);
            Dictionary<Guid, ActorSnapshot> actors = actorRows.ToDictionary(item => item.UserId,
                item => new ActorSnapshot(item.DisplayName, item.Active ? item.UserName ?? "Сотрудник" : $"{item.UserName ?? "Сотрудник"} · архивирован"));
            foreach (var item in actorRows) references[item.UserId] = item.DisplayName;

            Guid[] ids = rows.Select(item => item.ObjectId).Distinct().ToArray();
            foreach (var item in await db.Organizations.AsNoTracking().Where(item => item.Id == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("Organization", item.Id, "Организация", item.Name, false);
            foreach (var item in await db.Employees.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("Employee", item.Id, "Сотрудник", item.DisplayName, !item.Active);
            foreach (var item in await db.OrgUnits.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("OrgUnit", item.Id, "Отдел", item.Name, !item.Active);
            foreach (var item in await db.Teams.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("Team", item.Id, "Команда", item.Name, !item.Active);
            foreach (var item in await db.Positions.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("Position", item.Id, "Должность", item.Name, !item.Active);
            foreach (var item in await db.CollectorAgents.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("CollectorAgent", item.Id, "Парсер", item.Name, !item.Enabled);
            foreach (var item in await db.SearchConfigurations.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("SearchConfiguration", item.Id, "Поиск", item.Label, !item.Enabled);
            foreach (var item in await db.SearchGroups.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("SearchGroup", item.Id, "Группа поисков", item.Name, !item.Active);
            foreach (var item in await db.Listings.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("CatalogItem", item.Id, "Предложение", item.Title ?? item.Location ?? "Предложение без названия", false);
            foreach (var item in await db.PropertyCases.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("PropertyCase", item.Id, "Объект закупки", string.IsNullOrWhiteSpace(item.WorkingTitle) ? item.BusinessNumber : $"{item.BusinessNumber} · {item.WorkingTitle}", false);
            foreach (var item in await db.CaseCheckTemplateItems.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("CaseCheckTemplateItem", item.Id, "Шаблон проверки", item.Title, !item.Active);
            foreach (var item in await db.InspectionTemplateItems.AsNoTracking().Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id)).ToArrayAsync(cancellationToken)) Add("InspectionTemplateItem", item.Id, "Шаблон осмотра", item.Title, !item.Active);
            var jobs = await (from job in db.CollectionJobs.AsNoTracking() join search in db.SearchConfigurations.AsNoTracking() on job.SearchId equals search.Id where job.OrganizationId == organizationId && ids.Contains(job.Id) select new { job.Id, search.Label }).ToArrayAsync(cancellationToken);
            foreach (var item in jobs) Add("CollectionJob", item.Id, "Задание сбора", item.Label, false);

            var organizationReferences = await db.OrgUnits.AsNoTracking().Where(item => item.OrganizationId == organizationId).Select(item => new { item.Id, item.Name }).Concat(
                db.Teams.AsNoTracking().Where(item => item.OrganizationId == organizationId).Select(item => new { item.Id, item.Name })).Concat(
                db.Positions.AsNoTracking().Where(item => item.OrganizationId == organizationId).Select(item => new { item.Id, item.Name })).ToArrayAsync(cancellationToken);
            foreach (var item in organizationReferences) references[item.Id] = item.Name;
            foreach (var item in await db.Employees.AsNoTracking().Where(item => item.OrganizationId == organizationId).Select(item => new { item.Id, Name = item.DisplayName }).ToArrayAsync(cancellationToken)) references[item.Id] = item.Name;
            foreach (var item in await db.Roles.AsNoTracking().Select(item => new { item.Id, item.Name }).ToArrayAsync(cancellationToken)) references[item.Id] = item.Name ?? "Роль";
            return new(objects, actors, references);

            void Add(string rawType, Guid id, string displayType, string name, bool archived)
            {
                objects[Key(rawType, id)] = new(displayType, name, archived);
                references[id] = name;
            }
        }

        private static string SnapshotName(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                foreach (string property in new[] { "DisplayName", "Name", "Label", "Title", "BusinessNumber" })
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
            }
            catch (JsonException) { }
            return "";
        }

        private static string TypeLabel(string value) => value switch { "Organization" => "Организация", "Employee" => "Сотрудник", "OrgUnit" => "Отдел", "Team" => "Команда", "Position" => "Должность", "CollectorAgent" => "Парсер", "SearchConfiguration" => "Поиск", "SearchGroup" => "Группа поисков", "CollectionJob" => "Задание сбора", "CatalogItem" => "Предложение", "PropertyCase" => "Объект закупки", _ => "Системный объект" };
        private static string Key(string type, Guid id) => type + ":" + id.ToString("N");
    }
}
