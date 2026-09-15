# Stage 1 completion — Phase 2

Дата: 2026-09-16. Ветка: `codex/stage-1-procurement-core`.
База canonical master plan: `9810ff5b37b16a0670d24aa8cf94d6d6b6bcfa9a`.
Implementation commits: `ebf961f` (2A), `5757d50` (2B).

## Результат

Phase 2 `Collection shared pool и server-owned routing` завершена в обязательном
порядке 2A → 2B. Search больше не владеет назначением Collector, Department или
Team. Pending job создаётся без исполнителя, а совместимый локальный Collector
атомарно выбирается только во время claim с `FOR UPDATE SKIP LOCKED`. Lease
fencing, перевыдача истёкшей работы и фактический terminal executor сохранены.

Поверх shared pool добавлены тонкие `SearchGroup`, типизированные расписания
`Manual / Interval / FixedTimes`, идемпотентный server scheduler, ручной запуск,
счётчики результата и read model. Worker выполняет scheduler независимо от
Server. Экран «Сбор данных» показывает группы, поиски, расписания, общую очередь,
историю и состояние парсеров человекочитаемыми статусами; управление ограничено
organization scope и permissions `agents.manage` + `searches.manage`.

V1 wire endpoints и локальный Parser Agent не менялись. Ingestion создаёт
server-owned `CatalogSource` и не переносит routing ownership Search в Catalog.

## Миграции

- `20260915185630_Phase2ASharedCollectionPool`: nullable executor до claim,
  удаление routing FK/indexes Search, сохранение compatibility-only nullable
  columns без runtime usage.
- `20260915190710_Phase2BCollectionScheduling`: `collection.search_groups`,
  typed schedule fields, `next_run_at`, idempotency key `(search_id,
  scheduled_for)` и job counters.

Обе миграции schema-only. Старые migrations не переписывались; legacy dev/test
backfill намеренно отсутствует согласно решению владельца. Поддерживаемый путь —
полный chain на новой пустой PostgreSQL БД.

## Проверки

| Проверка | Результат |
|---|---|
| Release build всего solution, warnings as errors | green, 0 warnings / 0 errors |
| Phase 2A shared-pool concurrency/capability/lease tests | green |
| Phase 2B groups, typed schedules и concurrent scheduler ticks | green |
| clean DB полный migration chain / repeated migrate / zero / reapply | green, 9 migrations |
| `Database.HasPendingModelChanges()` | `false` |
| V1 HTTPS executable path, durable retry и ingestion | green |
| Server и Worker executable readiness/outage behavior | green |
| browser management flow и negative authorization | green, desktop/tablet |
| полный `LandErp.Foundation.Tests` | 16/16 green |

## Ограничения

- Старые локальные dev/test базы не обновляются и должны пересоздаваться.
- Compatibility-only nullable routing columns Search остаются физически до
  cleanup Phase 9, но runtime их не читает и не заполняет.
- Scheduler является single-database polling worker; распределённая блокировка
  обеспечивается PostgreSQL row locks и unique due-run index, отдельный job
  broker в Stage 1 не вводится.
- Production migration не применялась. Live marketplace smoke не выполнялся;
  wire/runtime path покрыт контролируемым V1 executable test.

Phase 3 не начиналась. Локальный Parser Agent не изучался и не изменялся.
