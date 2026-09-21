using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Server.Components;

public sealed record AccessV1Preset(string Id, string Name, EmployeeAccessConfiguration Settings);
public sealed record AccessV1NavigationState(bool Incoming, bool Procurement, bool Inspections, bool Collection, bool Audit);

public static class AccessV1Ui
{
    public static IReadOnlyList<AccessV1Preset> Presets { get; } =
    [
        new("incoming-operator", "Оператор входящих",
            new(IncomingAccessLevel.Process, ProcurementAccessLevel.None,
                AccessScope.Own, AccessScope.Own, CollectionAccessLevel.None,
                false, false, false, false, false)),
        new("procurement-manager", "Менеджер закупки",
            new(IncomingAccessLevel.None, ProcurementAccessLevel.Manager,
                AccessScope.AssignedObjects, AccessScope.AssignedObjects, CollectionAccessLevel.None,
                true, false, false, false, false)),
        new("procurement-head", "Руководитель закупки",
            new(IncomingAccessLevel.None, ProcurementAccessLevel.Head,
                AccessScope.Organization, AccessScope.Organization, CollectionAccessLevel.None,
                true, false, true, true, false)),
        new("inspector", "Осмотрщик",
            new(IncomingAccessLevel.None, ProcurementAccessLevel.None,
                AccessScope.Own, AccessScope.Own, CollectionAccessLevel.None,
                false, true, false, false, false)),
        new("procurement-collection", "Закупка + парсеры",
            new(IncomingAccessLevel.Process, ProcurementAccessLevel.Manager,
                AccessScope.Department, AccessScope.AssignedObjects, CollectionAccessLevel.Manage,
                true, false, false, false, false)),
        new("observer", "Наблюдатель",
            new(IncomingAccessLevel.Read, ProcurementAccessLevel.Read,
                AccessScope.Department, AccessScope.Own, CollectionAccessLevel.Read,
                false, false, false, false, false))
    ];

    public static AccessV1NavigationState Navigation(EffectiveEmployeeAccess? access) =>
        access == null
            ? new(false, false, false, false, false)
            : new(access.CanReadIncoming, access.CanReadProcurement,
                access.Settings.CanPerformInspections, access.CanReadCollection, access.CanReadAudit);

    public static string IncomingLabel(IncomingAccessLevel value) => value switch
    {
        IncomingAccessLevel.None => "Нет",
        IncomingAccessLevel.Read => "Просмотр",
        IncomingAccessLevel.Process => "Обработка",
        _ => value.ToString()
    };

    public static string ProcurementLabel(ProcurementAccessLevel value) => value switch
    {
        ProcurementAccessLevel.None => "Нет",
        ProcurementAccessLevel.Read => "Просмотр",
        ProcurementAccessLevel.Manager => "Менеджер",
        ProcurementAccessLevel.Head => "Руководитель",
        _ => value.ToString()
    };

    public static string CollectionLabel(CollectionAccessLevel value) => value switch
    {
        CollectionAccessLevel.None => "Нет",
        CollectionAccessLevel.Read => "Просмотр",
        CollectionAccessLevel.Manage => "Управление",
        _ => value.ToString()
    };

    public static string ScopeLabel(AccessScope value) => value switch
    {
        AccessScope.Own => "Свои",
        AccessScope.AssignedObjects => "Назначенные объекты",
        AccessScope.Team => "Команда",
        AccessScope.Department => "Отдел",
        AccessScope.Organization => "Организация",
        _ => value.ToString()
    };

    public static IReadOnlyList<string> Summary(EmployeeAccessConfiguration settings,
        EmployeeAccessSource source = EmployeeAccessSource.Configured)
    {
        if (source == EmployeeAccessSource.SystemOwner)
            return ["Системный полный доступ Owner. Обычные настройки Access V1 его не ограничивают."];

        List<string> result = [];
        if (settings.ProcurementAccess == ProcurementAccessLevel.None)
        {
            result.Add("Нет общего доступа к закупке.");
        }
        else
        {
            result.Add($"Видит: {ScopeSummary(settings.ProcurementReadScope)}.");
            if (settings.ProcurementAccess == ProcurementAccessLevel.Read)
                result.Add("Закупка доступна только для просмотра.");
            else
                result.Add($"Изменяет: {ScopeSummary(settings.ProcurementWorkScope)}.");

            if (settings.ProcurementAccess >= ProcurementAccessLevel.Manager
                && settings.ProcurementReadScope != AccessScope.Organization)
            {
                result.Add("Рабочие объекты всегда остаются видимыми, даже если находятся за пределами базовой области просмотра.");
            }
        }

        result.Add(settings.IncomingAccess switch
        {
            IncomingAccessLevel.Process => "Может обрабатывать входящие предложения.",
            IncomingAccessLevel.Read => "Входящие доступны только для просмотра.",
            _ => "Входящие предложения недоступны."
        });

        result.Add(settings.CollectionAccess switch
        {
            CollectionAccessLevel.Manage => "Может управлять поисками и парсерами.",
            CollectionAccessLevel.Read => "Поиски и парсеры доступны только для просмотра.",
            _ => "Поиски и парсеры недоступны."
        });

        if (settings.CanAssignInspections && settings.CanPerformInspections)
            result.Add("Может назначать и выполнять осмотры.");
        else if (settings.CanAssignInspections)
            result.Add("Может назначать осмотры, но не проводить их.");
        else if (settings.CanPerformInspections)
            result.Add("Может выполнять только назначенные ему осмотры.");
        else
            result.Add("Не может назначать или выполнять осмотры.");

        result.Add(settings.CanConfirmPurchase
            ? "Может фиксировать покупку доступного объекта."
            : "Не может фиксировать покупку.");

        if (settings.CanManageTemplates) result.Add("Может управлять общими шаблонами.");
        if (settings.CanReadAudit) result.Add("Может читать полный аудит.");
        return result;
    }

    public static IReadOnlyList<string> Compact(EmployeeAccessConfiguration settings)
    {
        List<string> result = [];
        if (settings.IncomingAccess != IncomingAccessLevel.None)
            result.Add("Входящие: " + IncomingLabel(settings.IncomingAccess));
        if (settings.ProcurementAccess != ProcurementAccessLevel.None)
            result.Add("Закупка: " + ProcurementLabel(settings.ProcurementAccess));
        if (settings.CollectionAccess != CollectionAccessLevel.None)
            result.Add("Парсеры: " + CollectionLabel(settings.CollectionAccess));
        if (settings.CanPerformInspections) result.Add("Осмотры");
        else if (settings.CanAssignInspections) result.Add("Назначает осмотры");
        if (settings.CanConfirmPurchase) result.Add("Покупка");
        if (settings.ProcurementAccess != ProcurementAccessLevel.None)
            result.Add($"Видит: {ScopeLabel(settings.ProcurementReadScope)} · работает: {ScopeLabel(settings.ProcurementWorkScope)}");
        return result.Count == 0 ? ["Нет рабочих доступов"] : result;
    }

    private static string ScopeSummary(AccessScope value) => value switch
    {
        AccessScope.Own => "только свои объекты",
        AccessScope.AssignedObjects => "назначенные ему объекты",
        AccessScope.Team => "объекты своей команды",
        AccessScope.Department => "все объекты своего отдела",
        AccessScope.Organization => "все объекты организации",
        _ => ScopeLabel(value)
    };
}
