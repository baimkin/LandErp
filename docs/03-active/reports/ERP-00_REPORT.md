# ERP-00 — отчёт документационного перехода

**Дата:** 2026-09-14.\
**Ветка:** `docs/erp-core-foundation`.\
**Baseline документации:** `82d4362`.\
**Checkpoint Collector:** `3994644d5a00413bb53513b78c5492c7e0b70692`.\
**Статус:** Completed — только ERP-00; ERP-01 подготовлен, не реализован.\
**Разрешение:** прямой запрос владельца на документационный/архитектурный переход,
затем уточнение источника комплекта в ветке `docs/erp-core-foundation`.

## 1. Результат

SPIKE-001 закрыт как исследовательская фаза. Conditional Go относится к
независимому техническому основанию ERP; общий Go и production-приёмка Collector
не объявлены. Отсутствующие эксперименты не помечены Passed.
Подтверждённые факты, гипотезы, исходные пороги и NotVerified —
в [итоге Spike](../SPIKE-001_RESULT.md).

Общий запрет Spike на Server/PostgreSQL/EFCore/Blazor снят в AGENTS.
Collector остаётся самостоятельным приложением по утверждённому ADR-007;
FP-004 и бизнес-карта приняты как обязательные вводные, не весь implementation scope.
ACTIVE_TASK указывает ровно на один [ERP-01](../GATE-ERP-01_Техническое_основание_и_БД.md)
со статусом «подготовлен; реализация не разрешена текущим запросом».
Production-код и migrations не создавались.

## 2. Изменённые документы и файлы

| Файл (относительно repository root) | Действие | Назначение |
|---|---|---|
| README.md | Изменён | Актуальная фаза, evidence Collector и расхождение веток |
| AGENTS.md | Изменён | Снятие устаревшего запрета, сохранение границ/ограничений |
| docs/START_HERE.md | Изменён | Экономный маршрут новой фазы |
| docs/03-active/ACTIVE_TASK.md | Изменён | Единственный следующий ERP-01 и Required reading |
| docs/04-foundation/ERP-00_Переход_от_Spike_к_production_ERP.md | Изменён | Фактический результат, Done, запрет автоматического старта |
| docs/04-foundation/FP-001_Архитектурный_каркас_и_среды.md | Изменён | Целевой полный план отделён от ограниченного ERP-01 |
| docs/04-foundation/FP-004_Сквозное_ERP-ядро_и_общие_бизнес-механизмы.md | Изменён | Статус принятой архитектурной вводной |
| docs/01-master/ERP_бизнес-карта_и_порядок_первой_реализации.md | Изменён | Статус вводной и явное разрешение отдельного Gate |
| docs/03-active/SPIKE-001_Проверка_жизнеспособности_парсинга_Avito.md | Изменён | Историческая программа закрыта с ограничениями, ссылка на итог |
| docs/03-active/SPIKE-001_RESULT.md | Создан | Итог по SPIKE-001_RESULT_TEMPLATE с evidence и пределами решения |
| docs/03-active/GATE-ERP-01_Техническое_основание_и_БД.md | Создан | Scope, карта файлов, conventions/migration review, checks и остановка |
| docs/03-active/reports/TP-LOCAL-001_MAP_DIAGNOSTIC_REPORT.md | Импортирован без изменения содержания | Последний фактический отчёт из checkpoint Collector, доступный в docs-ветке |
| docs/03-active/reports/ERP-00_REPORT.md | Создан | Этот отчёт по структуре GATE_REPORT_TEMPLATE, reuse и ревизия TP |
| docs/12-task-plans-foundation/TP-001_Каркас_solution.md | Изменён | Reuse/adapt, старый scope не активен |
| docs/12-task-plans-foundation/TP-002_Сборка_SDK_и_зависимости.md | Изменён | Reuse проверенных SDK/CPM/test settings |
| docs/12-task-plans-foundation/TP-003_PostgreSQL_и_миграции.md | Изменён | Adapt и review до первой migration, включая foundation |
| docs/12-task-plans-foundation/TP-004_Quality_Gates_и_наблюдаемость.md | Изменён | Reuse/adapt для technical checks |
| docs/12-task-plans-foundation/TP-005_Identity_и_каркас_прав.md | Изменён | Отложен; будущий ERP-02 требует нового scope |
| docs/12-task-plans-foundation/TP-006_Blazor_shell_и_UI_foundations.md | Изменён | UI отложен, не входит в ERP-01 |
| docs/12-task-plans-foundation/TP-007_Одна_поисковая_ссылка.md | Изменён | Adapt под ADR-007/ERP-04 |
| docs/12-task-plans-foundation/TP-008_Регистрация_Windows_Agent.md | Изменён | Adapt под самостоятельный Collector/ERP-04 |
| docs/12-task-plans-foundation/TP-009_Одно_задание_и_lease.md | Изменён | Adapt server job/lease/ack; не локальные claims |
| docs/12-task-plans-foundation/TP-010_Первая_фикстура_выдачи_Авито.md | Изменён | Первый parser scope устарел; reuse Collector tests |
| .gitignore | Изменён | Возвращено local-data/ исключение: SQLite/logs не попадают в Git |

Промежуточный ERP-00_PREPARATION_REPORT создан этой задачей до получения ветки
и удалён после подтверждения источника; пользовательские файлы не удалялись.
ADR-007 уже утверждён в полученном комплекте, его содержание не менялось.

## 3. Collector: reuse / adapt / experimental

Классификация выполнена по implementation reports/карте модулей и inventory
checkpoint, без полного повторного code review. Это решение о сохранении
компонентов, а не автоматическое разрешение копировать их в production.
Reuse сохраняет работающую реализацию Collector; любой перенос требует review.

Все source paths ниже относительно `src/LandErp.ParserSpike/` в checkpoint,
Desktop — `src/LandErp.ParserSpike.Desktop/`; здесь эти исходники не импортированы.

| Категория | Существующая часть | Решение/ограничение |
|---|---|---|
| reuse | Contracts/, Serialization/, CLI и принятые regression tests | Сохранить совместимость Gate01; Spike JSON не объявлять server wire contract |
| reuse | Raw/Parsed/Presence, classification/reason codes и чистая типизация | Сохранить semantic invariants и negative tests; бизнес-факт не выводить из seller claim |
| reuse | Source+ExternalId, observations vs latest known, A→B→A history, missing/order rules | Сохранить проверенное поведение локального store; cross-source объявления не сливать автоматически |
| reuse | Sanitised synthetic fixtures/ожидания и tests ID/URL/main-only/conflicts/secret exclusion | Сохранить regression material; original source HTML не копировать в Git |
| reuse | Полезный WPF local UI/settings/search/history/export | Сохранить работающий самостоятельный local mode, не писать его заново ради ERP |
| adapt | LocalCollection/Models.cs и интерфейсы jobs/events/results | Review версии contract/provenance/capabilities перед server adapter; delivery ID отличается от listing ID |
| adapt | LocalCollection/LocalStore.cs | Остаётся SQLite durability Collector; будущий outbox/ack отдельной задачей, без прямого sync/PostgreSQL connection |
| adapt | LocalCollection/QueueRunner.cs, SearchUrls.cs, local claims/checkpoints/freshness | Сохранить local mode; server IJobSource/leases/HTTP adapter позже, не подменять server lease локальным claim |
| adapt | LocalCollection/BrowserSessions.cs, Desktop/WorkspaceController.cs и диагностика | Lifetime/packaging/configuration и retention отдельно пересмотреть; browser stays Collector, не Server |
| experimental | DOM/JSON selectors и navigation/pagination в DomSourcePage.cs | Offline-проверенная source logic сохраняется Collector; live-устойчивость новых DOM/JSON не гарантируется |
| experimental | AvitoMapCapture.cs: drawId/контур/precision/totalCount | Один live-сеанс не доказывает полноту; precision не метры, totalCount требует отдельной проверки |
| experimental | Wheel/loading/end heuristics, localPriority, Avito на настроенном limit | Открытые operational проблемы; не переносить как production guarantee |
| experimental | Прежние AvitoBrowser/SearchCollector/MainWindow/ListingStore, repo-local paths/debug artifacts | Сохранить regression contour; не production runtime foundation; profiles/secrets/data вне Git |

ADR-007 сохраняет ответственность Server за серверные CollectionJob/leases/result
validation/catalog/history и Collector за browser/local queue/SQLite.
Эти будущие server функции не отменяются самостоятельностью Collector,
но в ERP-01 не реализуются. Серверу передаются API/contracts, не профили/credentials браузера.

## 4. Ревизия старых TP-001–TP-010

| TP | Итог | Что переиспользуется / что устарело |
|---|---|---|
| TP-001 | adapt → ERP-01 | Project boundaries полезны; одновременные Agent/AgentContracts/все пять test projects не входят в первый Gate |
| TP-002 | reuse/adapt → ERP-01 | SDK/CPM/locks/analyzers уже проверены в Collector; bootstrap не повторять; xUnit/Testcontainers — кандидаты, MSTest не заменять автоматически |
| TP-003 | adapt → ERP-01 | Npgsql/DbContext/design-time/migration checks; старую служебную migration не генерировать до conventions review; schema naming до первой production migration |
| TP-004 | reuse/adapt → ERP-01 | Health/logs/Problem Details/correlation/boundaries; без collection Worker jobs и полной инфраструктурной платформы |
| TP-005 | deferred → отдельный ERP-02 | Permission/security/audit идеи полезны; login scope требует Organization/visibility уточнения, не входит в ERP-01 |
| TP-006 | deferred → будущий UI use case | ADR-006 CSS/tokens сохраняются; UI shell/showcase не активирован |
| TP-007 | adapt → ERP-04 | SearchRevision/URL/idempotency полезны; ownership Collection допустим ADR-007, server/UI/transport перепланируются |
| TP-008 | adapt → ERP-04 | Machine identity/revoke/heartbeat полезны; existing Collector получает adapter, не пересоздаётся как Agent |
| TP-009 | adapt → ERP-04 | Server jobs/lease/ResultId/ack нужны по ADR-007; local SQLite claims не этот протокол; интеграция отдельно |
| TP-010 | первый parser scope устарел; reuse tests | Classifier/search parsing уже есть; sanitised fixtures/provenance/negative checks сохранить в Collector, source parser в Server не создавать |

Старые snippets и зависимости оставлены как исторический материал с явными
баннерами. Ни один TP не активирован; полная проверка его examples проводится
только перед соответствующим будущим implementation scope.

## 5. Точный scope ERP-01 и решения до migration

Единственный подготовленный Gate:
Server/Application/Infrastructure/Worker skeleton, reproducible .NET10/C#14 build,
один основной DbContext/Npgsql/PostgreSQL, design-time/config/data conventions,
schema-only technical migration после review, health/logging/Problem Details,
architecture и real-DB migration/integration tests. Worker — host без jobs.
Первое применение только disposable Local/Test; production apply отдельно.

Не входят Identity/Organization/Workflow/UI/бизнес-модули/Collector integration,
новый Agent/contracts, queues/leases/outbox/scheduler, PostGIS, SQLite import,
deployment/installer. Production migrations в ERP-00 отсутствуют.

Полный финальный checklist — §5 ERP-01:
база implementation-ветки и reuse build settings; совместимые patch-версии и
license/support/advisories; один DbContext/schema ownership/history/naming/DDL;
uuid/UUIDv7 vs ExternalId/BusinessNumber; UTC/timestamptz/timezone и
RecordedAt/EffectiveAt/ObservedAt; decimal/currency/precision/rounding;
concurrency/approval version; append-only facts/no runtime deletes/no full
Event Sourcing; presence/provenance/order/idempotency; flexible stages vs strict facts;
runtime/migrator roles/secrets; immutable applied migrations/SQL review/clean DB/
repeat/upgrade when applicable/schema drift; backup/restore/forward recovery/locks/
app-schema compatibility и отдельное разрешение production apply.

Foundation review фиксируется до генерации, полученный SQL проверяется отдельно.
Будущие бизнес-таблицы не создаются ради применения этих conventions.

## 6. Выполненные проверки

| Проверка | Результат / evidence |
|---|---|
| Обязательный маршрут | README → START_HERE → ACTIVE_TASK → required docs; после уточнения ветки маршрут восстановлен по её файлам |
| Последний фактический отчёт | Прочитан из Collector checkpoint; импортирован для локального чтения в docs-ветку |
| Источник комплекта | git fetch указанной владельцем docs/erp-core-foundation и git switch tracking branch; без merge/rebase/history rewrite |
| Collector inventory | git ls-tree checkpoint: фактические modules/tests; код не импортирован и не изменён |
| Согласованность scope | Passed: один следующий ERP-01, явный запрет реализации, foundation scope без business/UI/integration; адресное чтение ACTIVE_TASK/ERP-00/Gate |
| Markdown ссылки/импорт evidence | Passed: 52 локальные ссылки в 23 документах, отсутствующих целей нет; содержание фактического отчёта совпало с checkpoint при нормализации переводов строк |
| git diff --check / security scope | Passed: tracked diff без whitespace ошибок; четыре новых документа также проверены git diff --no-index --check. Первый прогон выявил trailing spaces Markdown hard breaks: заменены на явные переносы. Только 23 документа и .gitignore; local-data/browser-profiles ignored, tracked data нет |
| Build/tests/production migration | Не запускались и не создавались: задача исключительно документационная |
| Ранее выполненный Collector checkpoint | 85 offline passed, Release без warnings/errors; это исторический evidence, не новый прогон ERP-00; NuGetAudit=false не новый security approval |

Проверки ссылок, scope и импорта фактически выполнены после записи файлов.
Ветка Collector и origin/ветка Collector обе остались на исходном checkpoint.

## 7. Отклонения, исправленные недоработки и ограничения

- Комплект отсутствовал в стартовой ветке, но получен по уточнению владельца;
  промежуточное препятствие устранено. Docs branch baseline предшествует коду.
- README/ACTIVE_TASK ошибочно представляли Gate01 как невыполненный:
  исправлено по принятому commit и последнему Collector checkpoint.
- ERP-00 исходно заявлял live Avito/Cian шире evidence: исправлено на точные
  факты, Cian full live acceptance и исходные field-level thresholds не выдуманы.
- Исходная фраза «после ERP-00 можно начинать ERP-01» могла разрешить
  автоматический старт: явно требуется отдельное утверждение.
- FP-001 полный каркас/Agent/PostGIS/CI и старые TP могли расширить первый Gate:
  добавлены ограничения ERP-01 и баннеры архивного материала.
- local-data/ оказался untracked в ранней docs-ветке: восстановлено Git exclusion,
  пользовательская база/журналы не читались, не менялись и не переносились.
- localPriority, Avito на limit и строгий критерий completeness карты остаются
  открытыми defects Collector; код не меняется в documentation transition.
- Вторая машина, длительная нагрузка, detail parser и полный live acceptance
  Collector — NotVerified/не реализованы. Это не блокирует отдельный ERP foundation.

## 8. Зависимости и безопасность

Новых пакетов, версий, production projects, migration/schema файлов нет.
Collector branch и checkpoint не изменены. Git merge/rebase/reset/commit/push не выполнялись.
Локальные data/profiles/secrets не импортированы; в документы перенесён только
публичный sanitised checkpoint report. .gitignore защищает local-data/browser profiles.

## 9. Следующий шаг

ERP-00 завершён. Отдельно утвердить ERP-01 и согласовать базу implementation-ветки,
сохранив проверенные общие build settings и независимый Collector.
До migration — review §5 Gate; production apply отдельно.
Не реализовывать ERP-01, ERP-02 или другие TP в текущем запросе.
