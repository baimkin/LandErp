# GATE-RELEASE-B2-01 — Audited PropertyCase corrections

**Package:** Release Package 02 — Correctable Business Data  
**Finding:** LR-13  
**Ветка:** `codex/release-package-02`  
**Исходный commit:** `92e952d0ecce6b5235b4c5d13dc0040dc2c236fc`

## Цель

Дать штатный server-side путь исправления ошибочного рабочего факта PropertyCase без правки БД вручную, создания дубля или изменения исходного Catalog source.

## Scope

Разрешено изменить только минимальный вертикальный срез:
- application contract correction-команды;
- `ProcurementWorkspace`;
- targeted PostgreSQL tests как код;
- ACTIVE_TASK / этот Gate / отчёт.

Исправляемые поля:
- WorkingTitle;
- WorkingPrice;
- WorkingAreaSquareMeters;
- WorkingLocation;
- CadastralNumber.

## Обязательные инварианты

1. Один вызов исправляет один явно выбранный рабочий факт.
2. Нужен `ExpectedCaseVersion`; stale version получает concurrency error.
3. Причина обязательна.
4. Авторизация выполняется на сервере через существующую Procurement permission/scope модель.
5. Audit хранит field + before + after + reason; actor/timestamp/correlation id даёт существующий audit-механизм.
6. Business Timeline получает человекочитаемый before → after и причину.
7. Catalog/Listings и source observations не изменяются.
8. Existing source discrepancy logic продолжает сравнивать источник с новым рабочим значением.
9. Не добавлять новую correction-таблицу: существующие append-only audit + timeline уже закрывают историю этого факта.

## Вне scope

- LR-14 unlink/relink;
- UI/UX B2-03;
- новые permissions/roles;
- миграции;
- merge cases / bulk correction;
- запуск build/tests/browser/Server/Worker.

По прямому решению владельца фактический прогон выполняется владельцем отдельно. В этом Gate наличие тестов не считается их прохождением.
