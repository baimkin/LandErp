# Активная задача LandErp

> **Активная работа с 17 сентября 2026 года:** Universal Parser по прямому запросу владельца. Канонический bounded plan: [`PARSER_UNIVERSAL_PRODUCT_PLAN.md`](PARSER_UNIVERSAL_PRODUCT_PLAN.md), протокол: [`COLLECTOR_SERVER_PROTOCOL_V1.md`](../05-collection/COLLECTOR_SERVER_PROTOCOL_V1.md), ветка: `codex/universal-parser`. Разрешены изменения самостоятельного Parser и необходимых Collector Server contracts/endpoints по checkpoints плана. Локальные группы и история автоматически на Server не переносятся; допускается только явное добавление одной выбранной ссылки с выбором server group и подтверждением. Production apply, live Avito/Cian, merge/rebase/force-push не разрешены. Старый указатель Stage 1 ниже сохраняется как исторический контекст и не ограничивает эту отдельно утверждённую работу.

> **Канонический execution-документ завершения Stage 1:** [`STAGE1_COMPLETION_MASTER_PLAN.md`](STAGE1_COMPLETION_MASTER_PLAN.md). **Phases 1–7 завершены и приняты владельцем**; Phase 8 `Operational Overview` реализована и ожидает приёмки владельцем, evidence: [`STAGE1_PHASE8_REPORT.md`](reports/STAGE1_PHASE8_REPORT.md). Рабочая ветка: `codex/stage1-phase8-overview`. Визуальный authority: [`landerp_overview_v1_4_market_per_group_production_fonts.html`](../14-ui-kit/prototypes/landerp_overview_v1_4_market_per_group_production_fonts.html). Phase 9 не начинать. Старые checkpoint/ERP-разделы ниже сохраняются как исторический контекст и не переопределяют master plan.

## P0 — обязательная следующая задача перед дальнейшим развитием

**Выполненный приоритет:** архитектурная граница `Catalog → Procurement` по документу [`P0_CATALOG_PROCUREMENT_BOUNDARY.md`](P0_CATALOG_PROCUREMENT_BOUNDARY.md).

Решение владельца от 2026-09-15: внешний `Listing`/объявление не является корнем закупочного процесса. Catalog — единый входящий список предложений из любых источников (`Avito`, `Cian`, `Telegram`, ручной ввод, знакомые, агенты и будущие интеграции). После `Взять в работу` создаётся самостоятельный `PropertyCase`, а входящий элемент сохраняется только как связанный источник данных.

P0 закрыт: Catalog является organization-shared входящим слоем, Procurement
case-centric, а `ListingId` сохранён только как deprecated compatibility adapter
до Phase 9. Кабинет инвестора и пост-покупочные модули не начинались.

Phase 7 интегрирована в `main` и принята владельцем 2026-09-17. Phase 8
реализуется от свежего `main`: один bounded Overview read service, полная замена
старой страницы `/`, server-side рынок по индивидуальным настройкам Search Group,
targeted проверки и forward migration без apply к `landerp_local`. Локальный
Parser Agent не изучать и не менять.

## Текущее разрешение — Stage 1

**Этап:** Stage 1 — Procurement Core.
**Статус:** Completed; A `03d940e`, B `699db19`, C `719a1c6`, D `50b9aba` завершены. Активного implementation checkpoint нет. Итог: [STAGE-1_REPORT](../../STAGE-1_REPORT.md). Stage 2 не активирован.
**Ветка:** `codex/stage-1-procurement-core`.

Прямой запрос владельца 2026-09-14 разрешает весь Stage 1, safe merge Collector/docs
в implementation-ветке, отдельные commits A–D и push. После успешных обязательных
проверок продолжать следующий checkpoint без подтверждения. Старые ограничения
ниже сохранены как история ERP-00 и не ограничивают утверждённый Stage 1.
Merge в main, force-push, rebase опубликованной истории, удаление веток,
production apply и Stage 2 не разрешены.

### Required reading Stage 1

Указанный ниже foundation комплект остаётся обязательным. Дополнительно:

- `STAGE-1_CHECKPOINTS.md` и `STAGE-1_DATA_CONVENTIONS.md`;
- перед B: ADR-004, FP-003, `docs/14-ui-kit/AGENT_UI_INSTRUCTIONS.md`,
  `ui-kit/tokens.css` и соответствующие примеры index.html/styles.css/app.js;
- FP-002 и FP-004 полностью для Identity/Organization/Workflow/business срезов.

Collector самостоятельный; Server без browser/WPF/SQLite. Только локальная
PostgreSQL 18, без Docker и без SQLite/InMemory вместо DB tests. Runtime без DDL,
startup migrations запрещены. Secrets и local data вне Git. Итог `STAGE-1_REPORT.md`
с фактическими командами, проверками, ограничениями и SHA; Stage 2 не начинать.

## Исторический указатель ERP-00 (заменён разрешением выше)

**Фаза:** production ERP; документационный переход ERP-00 выполнен 2026-09-14.\
**Единственный следующий Gate:** [ERP-01 — техническое основание и БД](GATE-ERP-01_Техническое_основание_и_БД.md).\
**Статус:** Подготовлен; реализация не разрешена текущим запросом.\
**Ветка подготовки:** `docs/erp-core-foundation`.\
**Ветка реализации:** `codex/erp-01-foundation` — планируемая, ещё не создана; база согласуется до записи кода.\
**Production-код / production migrations:** отсутствуют.

## Разрешение и исходная точка

Запрос владельца 2026-09-14 разрешил ERP-00 и подготовку ERP-01,
прямо запретив реализацию ERP-01. Этот указатель не является разрешением записи кода.
Для старта нужен отдельный прямой запрос выполнить ERP-01.

SPIKE-001 закрыт как исследовательская фаза; прежний общий запрет на
Server/PostgreSQL/EF Core/Blazor снят в AGENTS. Это не общий Go для Collector.
[Итог Spike](SPIKE-001_RESULT.md) и [отчёт ERP-00](reports/ERP-00_REPORT.md)
фиксируют фактические проверки, ограничения и классификацию reuse/adapt/experimental.

Collector сохранён в `spike/001-avito-viability` по checkpoint
`3994644d5a00413bb53513b78c5492c7e0b70692`; текущая документационная ветка
его исходники ещё не содержит. Не пересоздавать его, не менять main,
не выполнять merge/rebase без отдельного разрешения. Перед ERP-01 определить
базу implementation-ветки и reuse проверенных общих build/test настроек.

## Required reading ERP-01

После README → START_HERE → этого файла читать:

1. [AGENTS](../../AGENTS.md) — правила фазы и разрешения.
2. [ERP-01](GATE-ERP-01_Техническое_основание_и_БД.md) — весь Gate, включая checklist migration review.
3. [Комментарии объектов PostgreSQL](ERP-01_DB_COMMENT_CONVENTION.md) — обязательные русские comments в самой схеме БД для бизнес-таблиц, неочевидных бизнес-полей и важных технических объектов.
4. [Итог SPIKE-001](SPIKE-001_RESULT.md) — фактическая исходная точка и граница Collector.
5. [ADR-001](../02-decisions/ADR-001_Платформа_и_версии.md) — платформа; версии проверяются перед закреплением.
6. [ADR-002](../02-decisions/ADR-002_Структура_решения_и_границы_модулей.md) — references, один основной DbContext и владельцы данных.
7. [ADR-006](../02-decisions/ADR-006_Политика_зависимостей_и_UI-база.md) — зависимости; UI только как ограничение, не реализация.
8. [ADR-007](../02-decisions/ADR-007_Collector_как_самостоятельный_продукт_и_граница_с_Server.md) — весь документ.
9. [FP-001](../04-foundation/FP-001_Архитектурный_каркас_и_среды.md) — уточнение ERP-00, §§5–14, 16, 18: только foundation.
10. [FP-002](../04-foundation/FP-002_Идентификация_роли_права_и_аудит.md) — §§7, 13: future scope/audit invariants; Identity не реализовать.
11. [FP-004](../04-foundation/FP-004_Сквозное_ERP-ядро_и_общие_бизнес-механизмы.md) — §§1–2, 9, 14–17; правила денег/версий из §§6–7 только для review conventions.
12. [Бизнес-карта](../01-master/ERP_бизнес-карта_и_порядок_первой_реализации.md) — §§4, 7, ERP-01 в §8 и §10.

Отчёт ERP-00 читать при восстановлении контекста/review; старые TP и ADR-003/004/005
не добавлять в обязательное чтение ERP-01 без конкретной необходимости:
очередь, Identity и файлы сейчас не реализуются. Остальные будущие этапы не читать.

## Точный разрешаемый предмет следующего Gate

Минимальные Server/Application/Infrastructure/Worker skeleton, PostgreSQL/EF Core
persistence и design-time foundation, согласованные data conventions,
health/logging/Problem Details, architecture и real-DB migration/integration checks.
Технические имена объектов БД остаЎтся английскими; обязательные описания бизнес-таблиц,
неочевидных бизнес-полей и важных технических объектов хранятся в PostgreSQL как
русские schema comments и проверяются после применения migration.
Полная карта файлов и критерии — в ERP-01.

Никаких Identity/Organization/Workflow, Blazor UI, бизнес-модулей, новых Agent,
server adapter/registration/leases/outbox/серверной очереди, PostGIS или импорта SQLite.
Worker остаётся host skeleton. Финальный review conventions и начального DDL
предшествует генерации первой production migration; production apply отдельно разрешается.

## Остановка

ERP-00 завершён отчётом. ERP-01 сейчас не запускать.
После будущей реализации ERP-01 выполнить его проверки, создать отчёт и остановиться;
ERP-02 и остальные этапы не активируются автоматически. Commit/push не разрешены.
