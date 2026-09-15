# Stage 1 completion — Phase 1

Дата: 2026-09-15. Ветка: `codex/stage-1-procurement-core`.
База canonical master plan: `9810ff5b37b16a0670d24aa8cf94d6d6b6bcfa9a`.
Implementation commit: `f6c663a`.
Owner database-transition correction: [`../STAGE1_PHASE1_DATABASE_RESET_DECISION.md`](../STAGE1_PHASE1_DATABASE_RESET_DECISION.md).

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

## Реализация и migration contract

- Добавлены Catalog contracts, ручное создание, disposition и transactional
  `TakeToWork`/link-existing.
- Добавлены case-owned working facts и `PropertyCaseSourceLink`; confirmed source
  защищён filtered unique constraint и блокировкой исходной строки в транзакции.
- Migration `20260915160713_Phase1CatalogProcurementBoundary` теперь является
  **schema-only**: она создаёт новую boundary-схему, но не переносит pre-Phase-1
  dev/test data и не выполняет legacy backfill.
- Решением владельца pre-Phase-1 БД признаны disposable development state.
  Канонический переход — удалить старую dev БД и поднять новую полным migration
  chain. In-place upgrade legacy Stage 1 database не является Phase 1 contract.
- Nullable compatibility field `PropertyCase.ListingId` пока физически оставлен
  до cleanup Phase 9; новые business operations от него не зависят. Canonical
  source ownership существует только через `PropertyCaseSourceLink`.
- Исчезновение/изменение source не удаляет case и не перезаписывает его working
  facts. История, создаваемая уже новой Phase 1 моделью, сохраняется штатно.
- Добавлен минимальный `/incoming`: manual create, monitor/dismiss,
  «Взять в работу» и связь с существующим case. Queue/card переведены на CaseId.
- Локальный Parser Agent и Phase 2 не изучались и не изменялись.

## Проверки Phase 1

Базовая Phase 1 verification до database-transition correction выполнялась pinned
SDK 10.0.112 из `artifacts/stage1/dotnet/dotnet.exe`. PostgreSQL и браузерные
проверки запускались вне ограниченной песочницы.

После owner correction migration acceptance изменён: legacy-dataset regression
удалён из контракта, вместо него обязательна clean database migration и
`Database.HasPendingModelChanges() == false`.

| Проверка | Контракт |
|---|---|
| locked restore solution | required |
| Release build solution | required |
| Phase 1 ProcurementTests | 5 scenarios, включая clean DB migration |
| полный LandErp.Foundation.Tests | required |
| clean/repeated/zero/reapply migrations | required |
| legacy Stage 1 in-place migration | intentionally not supported; clean rebuild required |
| `Database.HasPendingModelChanges()` | должно быть `false` |
| concurrent/repeated `TakeToWork` | ровно один case/link/task/transition |
| browser executable flow | manual create → CaseId, mobile, legacy no-create, restart |
| backup/restore и runtime DDL isolation | required |

## Ограничения

Физические имена `Listing` и nullable compatibility field `PropertyCase.ListingId`
оставлены до доказанного cleanup Phase 9. Полный Incoming UX/monitoring относится
к Phase 3; реализован только обязательный Phase 1 slice. Live marketplace smoke
не выполнялся; существующий серверный Avito/Cian ingestion покрывается regression
test. Production migration не применялась. Phase 2 не начата.

После изменения migration старые локальные/dev базы не обновлять поверх старой
схемы: удалить и создать заново. Это осознанное owner-approved решение, а не
ограничение будущей production migration strategy.
