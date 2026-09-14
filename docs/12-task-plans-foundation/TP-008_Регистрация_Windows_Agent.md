# TP-008 — Регистрация одного Windows Agent

**Статус:** Требует адаптации под самостоятельный Collector/ERP-04; не активен\

> Ревизия 2026-09-14: Reuse machine identity/revoke, registration/heartbeat через server adapter; рабочий Collector не пересоздавать.
> [ERP-00 report](../03-active/reports/ERP-00_REPORT.md) — итоговая ревизия.
> Примеры ниже — исторический материал, не approved implementation. Текущий
> scope задаёт ACTIVE_TASK; ни этот TP, ни старые зависимости не разрешают код.
**Зависимости:** TP-003–TP-005, ADR-004, FP-011, OP-010  
**Результат:** администратор выдаёт одноразовый код, а Windows x64 Agent получает отдельную отзываемую identity.

## Входит

- registry `AgentDevice` и одноразовый `AgentRegistrationCode`;
- admin use case создания кода;
- HTTPS endpoint регистрации;
- verifier/hash server-side;
- локальное защищённое хранение credential за интерфейсом;
- heartbeat с версией, OS, architecture и capabilities;
- revoke устройства;
- аудит без секрета.

Установка service, автообновление, browser profile и получение задач не входят.

## Эталон контракта

```csharp
public sealed record RegisterAgentRequest(
    string RegistrationCode,
    string DeviceName,
    string AgentVersion,
    string OperatingSystem,
    string Architecture);

public sealed record RegisterAgentResponse(
    Guid AgentId,
    string Credential,
    DateTimeOffset IssuedAt);
```

`Credential` возвращается ровно один раз и никогда не логируется.

```csharp
public interface IAgentCredentialStore
{
    ValueTask SaveAsync(string credential, CancellationToken cancellationToken);
    ValueTask<string?> ReadAsync(CancellationToken cancellationToken);
    ValueTask DeleteAsync(CancellationToken cancellationToken);
}
```

Windows-реализация выбирает защищённое хранилище ОС; серверный контракт от неё не зависит.

## Проверки

- одноразовый код работает один раз и истекает;
- две машины не получают общий credential;
- heartbeat от revoked Agent отклоняется;
- credential отсутствует в логах, audit и exception details;
- Windows store проходит локальный smoke-test;
- Agent не имеет доступа к PostgreSQL.

## Definition of Done

Один тестовый Windows x64 Agent виден администратору как Online/Idle, может быть
отозван и не имеет бизнес-разрешений сотрудника.

