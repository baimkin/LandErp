# TP-009 — Одно сохраняемое задание и lease

**Статус:** Требует адаптации под ADR-007/ERP-04; не активен\

> Ревизия 2026-09-14: Server job/lease/ack по ADR-007 сохраняются; локальные claims не серверные leases, ResultId не ExternalId; integration/outbox отдельно.
> [ERP-00 report](../03-active/reports/ERP-00_REPORT.md) — итоговая ревизия.
> Примеры ниже — исторический материал, не approved implementation. Текущий
> scope задаёт ACTIVE_TASK; ни этот TP, ни старые зависимости не разрешают код.
**Зависимости:** TP-008, ADR-003, FP-011  
**Результат:** Server атомарно выдаёт зарегистрированному Agent одно тестовое задание и идемпотентно принимает результат.

## Входит

- `CollectionTask`, `TaskLease`, `AgentExecutionResult`;
- создание `ValidateAgentSession` task администратором;
- `LeaseNextTask`, `StartTask`, `CompleteTask`;
- lease expiry и простой reaper;
- Agent SQLite outbox;
- уникальность `(AgentId, ResultId)`;
- метрики глубины очереди, lease и completion.

Retry matrix, P0–P4, Avito payload и несколько Agent откладываются.

## Эталон атомарной выдачи

```sql
WITH candidate AS (
    SELECT id
    FROM collection.tasks
    WHERE state = 'Pending' AND available_at <= now()
    ORDER BY priority, available_at, id
    FOR UPDATE SKIP LOCKED
    LIMIT 1
)
UPDATE collection.tasks AS task
SET state = 'Leased',
    lease_id = @lease_id,
    agent_id = @agent_id,
    lease_expires_at = @lease_expires_at
FROM candidate
WHERE task.id = candidate.id
RETURNING task.*;
```

Фактический SQL дополняется capability/source условиями и проверяется на PostgreSQL 18.

## Эталон результата

```csharp
public sealed record CompleteTaskRequest(
    Guid TaskId,
    Guid LeaseId,
    Guid ResultId,
    DateTimeOffset ObservedAt,
    string OutcomeCode,
    int PayloadVersion);
```

## Проверки

- два конкурентных lease не получают одну задачу;
- completion повторяется безопасно;
- чужой/revoked Agent и неверный LeaseId отклоняются;
- истёкший lease возвращается в очередь по политике;
- сетевой сбой сохраняет результат в SQLite outbox;
- очистка outbox происходит только после server acknowledgement.

## Definition of Done

Сквозной сценарий task → lease → outbox → complete → acknowledge воспроизводим и
покрыт integration/system test без прямого доступа Agent к БД.

