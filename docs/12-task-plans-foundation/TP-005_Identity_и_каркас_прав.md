# TP-005 — Identity и каркас разрешений

**Статус:** Отложен после ERP-01; материал для ERP-02, требуется отдельный Gate\

> Ревизия 2026-09-14: Identity/Organization/visibility/audit не входят в ERP-01; старый минимальный login не заменяет уточнённую модель ERP-02.
> [ERP-00 report](../03-active/reports/ERP-00_REPORT.md) — итоговая ревизия.
> Примеры ниже — исторический материал, не approved implementation. Текущий
> scope задаёт ACTIVE_TASK; ни этот TP, ни старые зависимости не разрешают код.
**Зависимости:** TP-003, TP-004, ADR-004, FP-002  
**Результат:** приглашённый сотрудник входит в ERP, а сервер проверяет именованные permissions.

## Входит

- ASP.NET Core Identity с cookie-сессией;
- закрытая регистрация через администраторское приглашение;
- роли Owner/Admin/Manager как стартовые наборы permissions;
- policy-based authorization;
- базовый audit входа, приглашения и изменения роли;
- seed только первого Owner через безопасную bootstrap-процедуру;
- отрицательный integration test;
- место для MFA без фиктивного production-провайдера.

Инвесторский project scope и Agent credential реализуются отдельными TP.

## Эталон permission requirement

```csharp
public sealed record PermissionRequirement(string Permission)
    : IAuthorizationRequirement;

public static class Permissions
{
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string SearchesRead = "searches.read";
    public const string SearchesManage = "searches.manage";
}
```

Endpoint запрашивает policy/permission; он не полагается только на проверку роли
в UI. Permission names централизованы и не создаются строками внутри страниц.

## Безопасность

- secure/HttpOnly/SameSite cookie по среде;
- antiforgery для изменяющих browser requests;
- rate limit входа/приглашения;
- токен приглашения одноразовый и имеет срок;
- пароль, recovery code и токен не логируются;
- изменение роли завершает/перепроверяет сессии по политике.

## Проверки

- invite → accept → sign in;
- истёкшее и повторное приглашение отклоняются;
- Manager не может управлять пользователями;
- неавторизованный запрос получает корректный 401/403;
- аудит содержит актор/действие, но не секрет.

## Definition of Done

Закрытый вход и permission boundary работают end-to-end; отсутствие выбранного
MFA-провайдера явно блокирует Production, но не Local/Test.

