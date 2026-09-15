# Stage 1 completion — Phase 1

Дата: 2026-09-15. Ветка: `codex/stage-1-procurement-core`.
База canonical master plan: `9810ff5b37b16a0670d24aa8cf94d6d6b6bcfa9a`.
Implementation commit: `f6c663a`.

## Результат

Phase 1 `Catalog / Procurement boundary` завершена до exit gate. Catalog стал
универсальным organization-shared входящим слоем с server-owned source codes,
optional external identity и ручным Telegram-like ingress без fake Collector,
Job или Observation. `PropertyCase` является самостоятельным корнем Procurement,
читается без источников и поддерживает `0..N` confirmed source links.

Procurement queue, card, workflow commands, notifications и canonical route
используют `CaseId`. Scope `Own / AssignedObjects / Team / Department /
Organization` вычисляется через responsibility и assignment самого case, а не
через metadata Listing. Deprecated `/procurement/listings/{ListingId}` только
разрешает существующую confirmed связь и не создаёт case неявно.

## Реализация и миграция

- Добавлены Catalog contracts, ручное создание, disposition и transactional
  `TakeToWork`/link-existing.
- Добавлены case-owned working facts и `PropertyCaseSourceLink`; confirmed source
  защищён filtered unique constraint и блокировкой исходной строки в транзакции.
- Новая migration `20260915160713_Phase1CatalogProcurementBoundary` выполняет
  expand/backfill/cutover: переносит working facts и responsibility, создаёт
  confirmed links с migration provenance и снимает обязательную связь
  `PropertyCase.ListingId`. Старые migrations не изменялись.
- История BusinessNumber, assignment, tasks, approvals, transitions, timeline,
  audit, Listing и Observation сохраняется; disappearance/change source не
  удаляет case и не перезаписывает его working facts.
- Добавлен минимальный `/incoming`: manual create, monitor/dismiss,
  «Взять в работу» и связь с существующим case. Queue/card переведены на CaseId.
- Локальный Parser Agent и Phase 2 не изучались и не изменялись.

## Проверки

Использован pinned SDK 10.0.112 из `artifacts/stage1/dotnet/dotnet.exe`.
PostgreSQL и браузерные проверки запускались вне ограниченной песочницы, поскольку
локальные dev-certificate/DataProtection keys недоступны внутри неё.

| Проверка | Результат |
|---|---|
| locked restore solution | success |
| Release build solution в isolated artifacts | 0 warnings, 0 errors |
| Phase 1 ProcurementTests | 5/5 |
| полный LandErp.Foundation.Tests | 14/14 |
| clean/repeated/zero/reapply migrations | green |
| legacy Stage 1 migration | facts, links, scope, history и provenance сохранены |
| `Database.HasPendingModelChanges()` | `false` |
| concurrent/repeated `TakeToWork` | ровно один case/link/task/transition |
| browser executable flow | manual create → CaseId, mobile, legacy no-create, restart green |
| backup/restore и runtime DDL isolation | green |

## Ограничения

Физические имена `Listing` и nullable compatibility field `PropertyCase.ListingId`
оставлены до доказанного cleanup Phase 9. Полный Incoming UX/monitoring относится
к Phase 3; реализован только обязательный Phase 1 slice. Live marketplace smoke
не выполнялся; существующий серверный Avito/Cian ingestion покрыт regression test.
Production migration не применялась. Phase 2 не начата.

Стандартный Release output занят ранее запущенным пользовательским экземпляром
`LandErp.Server`, поэтому финальная сборка выполнена с отдельным
`--artifacts-path artifacts/phase1`; это не влияет на исходники или verification.
