# Stage 1 completion — Phase 3

Дата: 2026-09-16. Ветка: `codex/stage-1-procurement-core`.
Baseline с актуальной migration policy: `f02b107b69549abb3253d7a396ef3802b77fed87`.

## Результат

Phase 3 `полный Incoming Catalog + отдельная Procurement queue` завершена до exit
gate. Incoming получил server paging, text/source/status/age/price/area/attention
filters, сводные счётчики и detail drawer. Ручные предложения без URL и ExternalId
проходят тот же первичный flow, что автоматические источники. Навигация сохраняет
раздельные `Входящие` и `Закупка`, а Procurement queue остаётся PropertyCase-based.

Классификации `Duplicate`, `Fake`, `RemovedAtSource`, `Sold` и обычное business
dismissal различаются в модели и UI. История изменения источника, мониторинга,
классификации и возобновления хранится append-only в `catalog.events`; provenance,
observations и source links не удаляются.

Мониторинг хранит независимые пороги общей цены и цены за сотку. Заполненные
условия применяются по утверждённому правилу ИЛИ. Новое Collector observation
фиксирует использованные source values и время оценки; достигнутый порог возвращает
item в active Incoming/attention. Source revision не изменяет working facts уже
созданного PropertyCase.

Для связанного rejected/monitor Case добавлена explicit resume-команда. Она
возобновляет тот же PropertyCase, assignment/task, workflow, timeline и audit без
создания второго case или source link. Server-side organization/case scope и
negative tenant tests входят в Phase 3.

## Phase 2 verification hardening

- прямой regression: `Agent A claim -> lease expired -> Agent B reclaim -> stale
  Agent A AcceptAsync rejected`;
- отдельные behavioural tests `Manual`, `Interval`, `FixedTimes`;
- точечный прогон после добавления: `5/5` green;
- production-дефект Phase 2 не обнаружен; production-код Phase 2 не исправлялся.

## Миграция

- `20260915224431_Phase3IncomingCatalog`;
- forward-only поверх опубликованной цепочки, старые migrations не изменялись;
- добавлены monitoring/attention columns в `catalog.listings` и immutable
  `catalog.events` с FK, index и русскими schema comments;
- runtime role получает только необходимые `SELECT/INSERT` для events;
- полный clean chain содержит 10 migrations.

## Verification

| Проверка | Результат |
|---|---|
| Release build всего solution | green, 0 warnings / 0 errors |
| Связанные Phase 1/2/3 suites | green, 14/14 |
| Полный `LandErp.Foundation.Tests` | green, 24/24 |
| Clean PostgreSQL migration chain + repeated migrate | green, 10 migrations |
| Rollback до `0` + полный reapply | green |
| PostgreSQL comments и runtime DDL isolation | green |
| Backup/restore с `catalog.events` | green |
| `Database.HasPendingModelChanges()` / EF pending-model command | `false` / no changes |
| Server/Worker readiness и outage behavior | green |
| V1 Collector executable/durable delivery path | green |
| Incoming desktop/mobile browser scenarios | green |

Browser evidence сохранён в ignored `artifacts/stage1/phase3-ui/` и не добавлен в Git.

## Ограничения

- До первого production deployment действует pre-production clean-rebuild policy
  master plan; production apply не выполнялся.
- Автоматическая переоценка monitoring происходит при новом observation источника.
  Для ручных/Telegram-like items без integration revision порог хранится, но внешний
  источник не опрашивается сервером самостоятельно.
- Rule engine не вводился: утверждённая семантика двух заполненных порогов — ИЛИ.
- Полные переговоры, dossier/checks/attachments относятся к Phase 4 и не начинались.
- Локальный Parser Agent не изучался и не изменялся.

Phase 3 exit gate выполнен. Phase 4 не активирована и не начиналась.
