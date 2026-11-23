# Независимый аудит плана ускорения Incoming

Дата: 23.11.2026. Gate: INCOMING-FILTER-PLAN-AUDIT. Только анализ и документы.
Checkout: C:\.Projects\LandErp, ветка `codex/inspection-file-improvements`,
HEAD `c6a79c725d57064732a6676e66cfa7d862fe6bff`. Код исследован в текущем dirty
checkout, а не только в HEAD; накопленные изменения не приписываются аудиту.

## Вердикт

**Исходный проект двух membership-таблиц и трёх столбцов пресета не готов к
реализации.** Он не определяет корректную публикацию при конкурентных изменениях,
динамические условия и правила duplicate ingress. Кроме того, нет доказательства,
что полный precompute необходим для устранения обнаруженной задержки.

**Исправленный минимальный пакет A готов к отдельному разрешению реализации**:
отделить rows+exact Total от информационных чисел/рынка, исправить форму запросов
и batch metadata. Миграции и дополнительная работа ingress ему не нужны.
[Полный исправленный план](../docs/03-active/PLAN-INCOMING-FILTER-PRECOMPUTE.md)
содержит критерии, контракты, concurrency, файлы, проверки и условия остановки.
Быстродействие нового SQL ещё предстоит доказать измерениями.

Одна listing↔search проекция оставлена **условным B**, если после A история всё
ещё ограничивает скорость. Её схема/backfill/readiness/откат описаны конкретно.
Filter memberships/revisions/generations/queue не входят в выбранный вариант.
Реализация и следующие Gates этим аудитом не начаты и не разрешены.

## Findings P0–P2

P0 не обнаружены в рамках read-only аудита плана. Ниже P1 блокируют реализацию
исходного проекта; P2 требуют учёта в выбранном пакете или отдельного ограничения.
Ссылки относятся к текущему содержимому файлов 23.11.2026. Это findings проекта
и подтверждённые границы нынешнего кода, а не заявление о новых production incident.

### P1-01. Ревизия criteria не защищает от потерянного обновления listing

Evidence: `src/LandErp.Infrastructure/Modules/Collection/CollectorGateway.cs:360–403`
обновляет current listing и добавляет observation в ingress transaction;
`src/LandErp.Infrastructure/Modules/Catalog/IncomingFilterPresetService.cs:133–157`
сохраняет новые criteria до commit. Между этими путями нет общего протокола
revision/generation или пересчёта membership.

Контрпример черновика: rebuild читает price=5m и исключает listing; параллельный
ingress меняет price=3m и включает его; publish старого rebuild удаляет новое
совпадение и ставит calculated_revision=criteria_revision. Все три столбца выглядят
«готовыми», но объявление потеряно. Атомарный DELETE+INSERT защищает от частичного
снимка, но не от этого lost update. Пользователь пропускает подходящий объект.

Исправление плана: постоянные filter sets исключены. Для A свежий snapshot read,
для B только монотонный max upsert фактов accepted history. Возвращение к полному
precompute потребует отдельного протокола, а не ещё одного calculated_at.

### P1-02. Записи только в CollectorGateway недостаточно для актуальности фильтра

Evidence: `IncomingCatalogQuery.cs:32–109` в каталоге
`src/LandErp.Infrastructure/Modules/Catalog/` использует disposition, attention,
price/area/land types/median, возраст и event-based presets;
`IncomingCatalogSelection.cs:62–95` — просмотр, классификацию и confirmed links.
`src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs:183–202`
создаёт ReviewStarted без обязательного изменения Listing.Version;
`:279–327` меняет disposition/monitoring/events, `:765–831` обрабатывает все
источники object group при передаче.

Возраст меняется без записи. ProcessedToday меняется в московскую полночь, а Age.Today
означает последние 24 часа. Одно поле Version listing или criteria не покрывает
эти зависимости. Пользователь увидит уже просмотренные «Новые», вчерашние
обработанные или неверные страницы. Цена/тип также изменяются после парсинга.

Исправление: полная таблица criteria/transient static/dynamic в §3 плана; в A/B
все бизнес-предикаты свежие, сохраняется только связь с историей в B. Никаких
часовых rebuild и инвалидации всего фильтра по каждому бизнес-событию.

### P1-03. Upsert на duplicate skip незаметно меняет принадлежность поиску

Evidence: `src/LandErp.Infrastructure/Modules/Collection/CollectorGateway.cs:308–313`
возвращает receipt до обработки; `:328–333` пропускает существующий ObservationKey;
`:356–358` пропускает одинаковый ListingId+ObservedAt+ContentHash независимо от
текущего search; новая history создаётся только `:391–403`.
`src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogQuery.cs:60–88`
выводит принадлежность исключительно из сохранённых observations/jobs.

Если такой duplicate доставлен в другом search, upsert текущего job.search
добавит новую зону, которой нет в нынешнем отборе. На replay могут повториться
business side effects, если связать «ремонт» projection с новой обработкой.
Это не обычная безопасная идемпотентность, а изменение правил отбора.

Исправление B: накапливать пары только реально вставляемых observations; upsert
после первого SaveChanges и до automation в той же транзакции; skip/replay не
создают новую пару. Прошлые факты восстанавливает отдельный backfill.

### P1-04. Связь с зоной не является неизменным свойством listing

Evidence: `IncomingCatalogQuery.cs:60–74`; `src/LandErp.Infrastructure/Modules/Collection/CollectionAdministration.cs:249–250,304–314`;
machine paths `CollectorGateway.cs:22–67` тоже меняют group/Active.
`IncomingFilterPresetService.cs:272–305` разрешает legacy/search/group compatibility.

Если готовый filter set содержит результат старой зоны, перенос search либо
архив/восстановление group меняет сразу исторический cohort без нового observation.
Обслуживать только зону текущего batch недостаточно. Пользователь получает чужую
зону/неполный набор; копирование group_id фиксировало бы неверную семантику.

Исправление: B хранит search_id и читает текущие group/Active; нет handler для
пересчёта всех зональных фильтров. Direct SearchConfigurationId и median permission
provenance не получают новое неявное требование Active.

### P1-05. Base membership count не равен Total, сумма фильтров не равна union

Evidence: `IncomingCatalogSelection.cs:103–143` строит union и replacement
selected/draft; `IncomingCatalogReadService.cs:63–77` определяет альтернативы и
AllPresetsCount. Тесты `tests/LandErp.Foundation.Tests/IncomingWorkingSelectionTests.cs:44–69,114–129`
проверяют пересечения, draft, замену зоны. Archive/Participants отдельно
нормализуются в `IncomingCatalogSelection.cs:21–45`.

Пример: фильтры дают {A,B,C} и {B,C}; сумма 5, правильный union 3. Из base set
после text/time может остаться один ID. Если вернуть базовое число как Total,
появятся пустые последние страницы и неверное «выбрать все». Draft, расширяющий
сохранённые условия, нельзя пересекать с прежним persisted set.

Исправление: одна формальная карта чисел в §2, DISTINCT ListingId до COUNT,
dynamic/transient до Total/order/pagination. Exact Total не заменяется window-only.

### P1-06. Технический rebuild не должен повторять AutoInclude бизнес-команду

Evidence: `IncomingFilterPresetService.cs:103,156,166–188` выполняет IncludeExisting
при сохранении opted-in фильтра. `CatalogCalculationAutomation.cs:12–38`
ограничивает IncludeNew только действительно созданными listing IDs;
`CollectorGateway.cs:389,453–457` вызывает это в ingress transaction;
`ProcurementWorkspace.cs:251–256` — в manual creation.

AutoIncludeNewInCalculation не является условием чтения. Rebuild/backfill,
который повторно вызывает существующее сохранение фильтра или IncludeNew на
неизменных listing, способен вернуть снятые пользователем галочки медианы и
создать лишнюю историю. Фильтр IncludedInMedian=false может также перестать
совпадать после законного IncludeExisting — это требует свежего read, не цикла
«пересчитать и ещё раз включить».

Исправление: технические операции чисты относительно business side effects;
автоматизация остаётся на прежних явных границах. Переименование не rebuild:
сейчас Rename повышает Version (`IncomingFilterPresetService.cs:191–210`).

### P1-07. Полный precompute пока не оправдан, а общий lock переносит задержку на запись

Evidence: `IncomingCatalogReadService.cs:55–78` повторяет current/union в summary,
альтернативах и группах. `IncomingCatalogQuery.cs:150–160` строит conditional
aggregates по всей организации. Baseline и EXPLAIN в предыдущем отчёте показывают
8.890 с count и 700 observation scan nodes для 50 фильтров, но не замер отдельного
COUNT(current) после исправления и не конкурентную ingress нагрузку.

Сериализация всех catalog writes с unbounded filter rebuild даёт точность ценой
ожидания приёма парсинга/сохранения. Существующий IncludeExisting сначала пишет
пресет, потом блокирует listing (`IncomingFilterPresetService.cs:136–156,182`);
добавление listing→preset lock в ingress создаёт риск обратного порядка.
Фоновая очередь без durable catch-up даёт иной риск — неверный «готовый» набор.

Исправление: A без нового write path. B — batch upsert пар без P-фактора.
Полная filter projection не выбрана; обещание скорости до реализации исключено.

### P2-01. Вторичные чтения пока блокируют показ страницы и масштабируются по истории

Evidence: `IncomingFilterPresetService.cs:40–52,272–305` делает resolver queries
по пресету. `IncomingCatalogReadService.cs:219–244` материализует observations и
events страницы до свёртки в памяти. `src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor:1480–1490`
ожидает весь read, при clamp повторяет его и затем ожидает рынок.

Пользователь может не увидеть готовые строки из-за timeout необязательного
счётчика/рынка. Маленькая страница не ограничивает объём materialized history.
Одна новая таблица сама эти накладные расходы не устраняет.
План A исправляет batch resolver, top-1/flags в SQL, clamp в основном snapshot
и независимые информационные состояния UI.

### P2-02. Нужны tenant-safe FK/grants и доказанная readiness, а не наличие строк

Evidence: `src/LandErp.Infrastructure/Modules/Collection/CollectionMappings.cs:96–105,160–167`
имеет listing PK и обычные FKs, observations без собственного OrganizationId;
`src/LandErp.Infrastructure/Persistence/ProductionDatabaseInitializer.cs:61–67`
перечисляет runtime tables явно. `ModelConventions.cs:234–242,302–315` задаёт
comments/names. Snapshot содержит observations(job_id), listings(org,changed_at),
search group index; «индексов нет» неверно.

Обычные FK listing_id/search_id сами не исключат cross-org pair. Новая таблица
без grants не заработает под runtime ролью. Неполный backfill не может считаться
готовым, особенно при старых одновременно работающих ingress writers. Количество
schema migrations нельзя вывести из количества колонок.
План B задаёт composite FK, parent alternate keys, DML grants, dual-write до
backfill, checkpoint-after-commit, two-way equality и явный per-org allowlist.

### P2-03. Свежая bulk проверка не доказывает сериализацию всех зависимостей

Evidence: `CatalogBulkSelectionReader.cs:16–40` читает свежий predicate и версии;
`CatalogArchiveService.cs:69–76` и `CatalogCalculationService.cs:83–92` затем
блокируют выбранные listing и сверяют stamp. Полный predicate после locks не
повторяется; `ProcurementWorkspace.cs:194–201` может изменить New через event без
Listing.Version, а move зоны меняет search.

Это существующая граница, а не внесённый аудитом дефект. Stamp защищает от
изменений версий выбранных строк, но не доказывает отсутствие всех group/time/event
races между selection и command commit. План не должен выдавать informational
snapshot за свежий набор команд. В A/B сохранён authoritative путь bulk; тесты
гонок обязательны перед более сильным обещанием. Общее перепроектирование bulk
не включено самовольно; при подтверждении строгого требования нужен отдельный scope.

## Подтверждённые факты

- Обязательный маршрут прочитан: README → START_HERE → ACTIVE_TASK → Gate аудита
  → Gate анализа → `reports/INCOMING_LOAD_TIMEOUT_REPORT.md` → FP-004 §§3,9,12–15.
  Предыдущий report физически находится в корневом reports, не docs/03-active/reports.
- Production Event 1101 из переданного evidence: IncomingCatalogV2/ReloadAsync/Read,
  InvalidOperationException, DB_TIMEOUT, inner TimeoutException, 10396 ms; health
  live/ready 200. Это timeout пути чтения. Конкретный production SQL не установлен.
- Текущий EF timeout 10 с: `Persistence/LandErpDbContext.cs:78–81`; он ограничивает
  отдельную команду, а не суммарную длительность page read.
- На synthetic 25k/500k/50: service 11031 ms, count 8890 ms; window 1009 ms без
  exact Total. `artifacts/incoming-load-timeout/measurements/baseline-25000-20.json`
  подтверждает service/window timings; count приведён в предыдущем отчёте.
- `artifacts/incoming-load-timeout/plan-summary.json`: 705 SubPlan/InitPlan,
  700 observation scan nodes, 5614 plan nodes. Это **одна SQL-команда**, не 700
  сетевых запросов; EXPLAIN без ANALYZE не измеряет actual loops/rows.
- В `tests/LandErp.Foundation.Tests/IncomingLoadTimeoutTests.cs:79–91,132–139`
  comparative read использует FixedPresets после реального чтения. Поэтому 1+P
  metadata commands не входят в его сравнительную latency.
- Адресно прочитаны TRX baseline-v2, analysis-baseline, analysis-growth: в каждом
  один test method Passed (1/1), не 7/4 отдельных MSTest tests. 7 и 4 в предыдущем
  отчёте — внутренние read-сценарии оснастки. Новых запусков здесь не было.
- Накопленные QuickDismiss/FastOpenTransfer существуют в текущих файлах и тестах;
  актуальность их развёртывания у владельца не установлена. Их код не изменён.

## Предположения и открытые решения

| Вопрос | Статус / когда решать |
|---|---|
| Причина именно production incident | Не установлена без SQL/plan/ожиданий; локальный тяжёлый путь согласуется с симптомами, но не исключает locks/I/O |
| Достаточно ли A | Гипотеза с конкретной future приёмкой; холодный COUNT(current) и новый informational SQL ещё не измерены |
| Необходимость B | Только если после A history cost остаётся значимым; сейчас не разрешать schema «на всякий случай» |
| Точное число B migrations | После size/lock/schema review на disposable; concurrent index preparation может потребовать отдельного шага |
| Строгая bulk concurrency | Текущие гарантии ограничены §P2-03; воспроизвести group/view/time races и уточнить требуемую границу до обещания сериализации |
| Числовая скорость | ≤3 с p95 main и ≤5 с команда на baseline — proposed цели; не результат/гарантия |
| Production SHA/schema/writers | Не проверены. Нужны владельцу перед rollout; старый writer после B dual-write инвалидирует readiness |
| Shared cache/worker | Исключены из A; локальный informational snapshot ≤60 с с freshAt. Если межпользовательская нагрузка докажет необходимость — отдельный ограниченный этап |

Эти неизвестные не блокировали порученные анализ/документы. Они блокируют
объявление нового SQL production-ready и запуск B без дополнительного решения.

## Что исключено ради простоты

Две постоянные таблицы результатов фильтров, ревизии/поколения, durable queue,
часовые/time-boundary jobs, Redis, общий distributed lock, фоновые полные обходы,
новые пакеты/extensions, перенос браузерной логики в Server, изменение median
формулы/галочек, physical merge listings и автоматический тяжёлый fallback.
В A не добавляются handlers в бизнес-команды и не меняется приём observations.
В B добавляется один batch projection upsert на accepted batch, без listing×filter
работы. Это удерживает стоимость записи независимой от числа пользовательских фильтров.

## Выполненные проверки и изменённые файлы

Фактически выполнены read-only `Get-Content`, адресные `rg/rg --files`, чтение
synthetic scalar JSON и XML counters TRX, `git status --short`, `git branch
--show-current`, `git rev-parse HEAD`, контроль SHA-256 ранее изменённых tracked
файлов и `.git/index`. Не читались credentials и raw production logs.
Технические предпосылки snapshot/CTE/upsert сверены с официальной документацией
PostgreSQL 18; ссылки находятся непосредственно в соответствующих разделах плана.

Записаны только:

1. `docs/03-active/PLAN-INCOMING-FILTER-PRECOMPUTE.md` — исправленный полный план.
2. `reports/INCOMING_FILTER_PLAN_AUDIT_REPORT.md` — этот аудит и вердикт.
3. `docs/03-active/GATE-INCOMING-FILTER-PLAN-AUDIT.md` — результат аудита.
4. Только секция INCOMING-FILTER-PLAN-AUDIT в `docs/03-active/ACTIVE_TASK.md` — статус.

Проверки документов выполнены: `git diff --check -- docs/03-active/ACTIVE_TASK.md`
прошёл; новые документы не содержат trailing whitespace; все 6 локальных Markdown
ссылок разрешаются. Проверены существующие полные source references и наличие
в плане всех criteria/transient полей. SHA-256 всех 75 ранее изменённых tracked
файлов, кроме разрешённой секции ACTIVE_TASK, и `.git/index` совпали с baseline.
После удаления ровно добавленного статусного блока байтовый SHA-256 ACTIVE_TASK
совпадает с исходным: остальные секции и их форматирование сохранены.
Build/tests/приложения/preview/БД/migrations не запускались по границе Gate.
Нагрузочные и concurrency проверки нового решения не выполнены: продукта ещё нет.
Git commit/push/deploy не выполнялись.

Следующий рекомендуемый шаг — отдельный запрос на ограниченный пакет A с
обязательными differential/performance checks. Сейчас аудит завершён;
реализацию, B и следующие Gates не начинать.
