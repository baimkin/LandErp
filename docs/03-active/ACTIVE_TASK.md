# Активная задача LandErp

## Текущий Gate — B4-01, Parser heartbeat / lease reliability

**Прямой запрос владельца от 18 сентября 2026 года:** начать B4-01.  
**Ветка:** `codex/release-package-04`.  
**Исходный commit Package 04 / B4-01:** `74bc3dd7f39b1d5dffd9911e8072973df56478bc`.  
**Scope:** LR-10 + только необходимые Parser lease/reclaim regressions.  
**Gate:** [GATE-RELEASE-B4-01](GATE-RELEASE-B4-01.md).  
**Evidence:** [RELEASE_PACKAGE_04_B4_01_REPORT](reports/RELEASE_PACKAGE_04_B4_01_REPORT.md).

B3-04 validation владелец выполняет самостоятельно; старт B4-01 не считается автоматическим принятием Package 03.

Не начинать B4-02 operational health, B4-03 deployment/backup или B4-04 UAT. Не переписывать shared queue/lease architecture без доказанного blocker. Фактические build/tests выполняет владелец; здесь они **Not run**.

### Required reading B4-01

После README → START_HERE → этого файла читать только:
1. Collector V1 protocol;
2. `CollectorGateway` heartbeat/claim/accept fencing;
3. `ServerCoordinator` heartbeat/recovery path;
4. `CollectionPoolTests` lease/reclaim suite;
5. ParserSpike server transport recovery tests.

## Предыдущий Gate — B3-03, inspection draft / conflict / server validation

**Прямой запрос владельца от 18 сентября 2026 года:** выполнить LR-06 + LR-07 + LR-12 вместе в B3-03.  
**Ветка:** `codex/release-package-03`.  
**Исходный commit B3-03:** `a500aecf3952c5c307082c1d172d1e60fade4e02`.  
**Scope:** integrity локального inspection draft, version-conflict UX и server-authority validation по snapshot semantics.  
**Gate:** [GATE-RELEASE-B3-03](GATE-RELEASE-B3-03.md).  
**Evidence:** [RELEASE_PACKAGE_03_B3_03_REPORT](reports/RELEASE_PACKAGE_03_B3_03_REPORT.md).

Не строить auto-merge между двумя осмотрами и не менять schema: при конфликте локальный draft сохраняется, save блокируется, переход на server version только явным discard. Не начинать B3-04 validation автоматически. Фактические build/tests/browser выполняет владелец; здесь они **Not run**.

### Required reading B3-03

После README → START_HERE → этого файла читать только:
1. SiteInspectionPage + `landErpInspection` local draft JS;
2. WorkspaceComponent command outcome;
3. `SaveInspectionAsync` + inspection snapshot domain;
4. Phase5/B3 targeted tests;
5. screen spec 04 только как interaction boundary.

## Предыдущий Gate — B3-02, file limits & safe upload errors

**Прямой запрос владельца от 18 сентября 2026 года:** начать B3-02 после публикации B3-01.  
**Ветка:** `codex/release-package-03`.  
**Исходный commit B3-02:** `5507a9e14a190a949fb9c683f6f4cec0aa1c31fd`.  
**Scope:** LR-11 + LR-20 — oversize inspection stream и честный raw/base64/HTTP/storage file-size contract.  
**Gate:** [GATE-RELEASE-B3-02](GATE-RELEASE-B3-02.md).  
**Evidence:** [RELEASE_PACKAGE_03_B3_02_REPORT](reports/RELEASE_PACKAGE_03_B3_02_REPORT.md).

Не начинать LR-06/LR-07/LR-12 inspection draft/conflict/semantic validation. Не менять пользовательский raw limit без необходимости: B3-02 сохраняет 8 MiB и согласует transport envelope. Фактические tests/build/browser выполняет владелец; здесь они **Not run**.

### Required reading B3-02

После README → START_HERE → этого файла читать только:
1. application FileStorage contract и общий file limit;
2. Server Kestrel/request handling + SafeExceptionHandler;
3. Procurement Add/Retry attachment validation;
4. CaseWorkspace и SiteInspectionPage upload handlers;
5. Yandex options и targeted file tests.

## Предыдущий Gate — B3-01, attachment lifecycle & recovery

**Прямой запрос владельца от 18 сентября 2026 года:** начать Package 03 после B2-04.  
**Ветка:** `codex/release-package-03`.  
**Исходный commit Package 03 / B3-01:** `936286ef53cc135bdc5c892d62b41332724cd882`.  
**Package:** Reliable Files & Field Inspection.  
**Scope B3-01:** только LR-08 — incomplete attachment recovery / PendingUpload.  
**Gate:** [GATE-RELEASE-B3-01](GATE-RELEASE-B3-01.md).  
**Evidence:** [RELEASE_PACKAGE_03_B3_01_REPORT](reports/RELEASE_PACKAGE_03_B3_01_REPORT.md).

Package 02 executable/manual acceptance остаётся pending owner validation, но владелец явно разрешил начать Package 03. Не считать это автоматическим принятием Package 02.

B3-01 использует существующий StoredFile lifecycle и stable FileId. Не строить новую storage subsystem, не добавлять migration, не начинать LR-11/LR-20 file limits и LR-06/LR-07/LR-12 inspection semantics. Фактические tests/build/browser владелец запускает самостоятельно; здесь они **Not run**.

### Required reading B3-01

После README → START_HERE → этого файла читать только:
1. application FileStorage contract;
2. current Procurement attachment add/read/retry path;
3. current storage providers и Yandex recovery tests;
4. attachment/document UI;
5. этот Gate и относящиеся targeted tests.

## Предыдущий Gate — B2-04, Package 02 validation handover

**Прямой запрос владельца от 18 сентября 2026 года:** начать B2-04 после публикации B2-03.  
**Ветка:** `codex/release-package-02`.  
**Исходный commit B2-04:** `45c29428a40d79482ee5afd0a5ee029378339460`.  
**Scope:** финальная статическая проверка B2-01–B2-03, P0 boundary review и точная validation-матрица владельцу.  
**Gate:** [GATE-RELEASE-B2-04](GATE-RELEASE-B2-04.md).  
**Package report:** [RELEASE_PACKAGE_02_REPORT](reports/RELEASE_PACKAGE_02_REPORT.md).

По решению владельца restore/build/PostgreSQL/browser/manual проверки выполняет владелец самостоятельно. Поэтому B2-04 не запускает их и не объявляет Passed. Новые production-механизмы в B2-04 не добавляются без реального blocker.

Статический review накопленного diff не выявил необходимости в migration, новой correction/history таблице, отдельном экране, новом permission или дополнительном API только ради Package 02. P0 Catalog → Procurement boundary сохранена.

### Required reading B2-04

После README → START_HERE → этого файла читать только:
1. `GATE-RELEASE-B2-01.md`, `GATE-RELEASE-B2-02.md`, `GATE-RELEASE-B2-03.md`;
2. `P0_CATALOG_PROCUREMENT_BOUNDARY.md`;
3. `GATE-RELEASE-B2-04.md` и package report;
4. накопленный diff Package 02 и относящиеся targeted tests.

## Предыдущий Gate — B2-03, UI/UX correction flows

**Прямой запрос владельца от 18 сентября 2026 года:** начать B2-03 после публикации B2-02.  
**Ветка:** `codex/release-package-02`.  
**Исходный commit B2-03:** `d00fd7fd5d49bf1c83fe8d3608f14d357b7c64de`.  
**Scope:** UI/UX для уже реализованных LR-13/LR-14: correction рабочих фактов, source unlink/relink, обязательная причина, before/after, stale-version recovery и отсутствие недоступных действий.  
**Gate:** [GATE-RELEASE-B2-03](GATE-RELEASE-B2-03.md).  
**Evidence:** [RELEASE_PACKAGE_02_B2_03_REPORT](reports/RELEASE_PACKAGE_02_B2_03_REPORT.md).

Не добавлять новые backend semantics, migration, новую permission-модель или LR-15 search/paging. Допустим только минимальный read-model флаг существующего `ManagerDecide`, необходимый для корректного скрытия source-link correction в UI. Фактические build/tests/browser проверки выполняет владелец; здесь они остаются **Not run**.

### Required reading B2-03

После README → START_HERE → этого файла читать только:
1. `GATE-RELEASE-B2-01.md` и `GATE-RELEASE-B2-02.md` как контракты уже реализованных команд;
2. `docs/14-ui-kit/screens/03-property-case.md` только как визуальную/экранную границу;
3. существующий `CaseWorkspace.razor`, ближайший CSS и browser scenario.

## Предыдущий Gate — B2-02, source unlink / relink correction

**Прямой запрос владельца от 18 сентября 2026 года:** начать B2-02 после публикации B2-01.  
**Ветка:** `codex/release-package-02`.  
**Исходный commit B2-02:** `c039600af0360e90a3731763f4380c8c06d0c779`.  
**Scope:** только LR-14 — штатное исправление ошибочной confirmed source relation: unlink и relink к другому PropertyCase с сохранением истории, server-side access, concurrency и audit/timeline.  
**Gate:** [GATE-RELEASE-B2-02](GATE-RELEASE-B2-02.md).  
**Evidence:** [RELEASE_PACKAGE_02_B2_02_REPORT](reports/RELEASE_PACKAGE_02_B2_02_REPORT.md).

Реализация должна использовать существующую модель `PropertyCaseSourceLink` и filtered unique constraint. Не добавлять migration/новую relation-history таблицу без доказанной необходимости. LR-15 search/paging, UI B2-03 и последующие packages не входят. Фактические build/tests/browser проверки выполняет владелец; здесь они остаются **Not run**.

### Required reading B2-02

После README → START_HERE → этого файла читать только:
1. `P0_CATALOG_PROCUREMENT_BOUNDARY.md`;
2. относящиеся к audit/timeline/access положения `docs/04-foundation/FP-004_Сквозное_ERP-ядро_и_общие_бизнес-механизмы.md`;
3. текущий Gate и только source-link/Catalog/Procurement код и targeted tests.

## Предыдущий Gate — B2-01, audited PropertyCase corrections

**Прямой запрос владельца от 18 сентября 2026 года:** начать Package 02 с B2-01 и решить LR-13 минимально достаточным изменением.  
**Ветка:** `codex/release-package-02`.  
**Исходный commit:** `92e952d0ecce6b5235b4c5d13dc0040dc2c236fc` — documentation/evidence HEAD Package 01.  
**Scope:** только LR-13 — штатное исправление рабочих фактов PropertyCase с причиной, optimistic concurrency, server-side permission, before/after, timeline и audit.  
**Gate:** [GATE-RELEASE-B2-01](GATE-RELEASE-B2-01.md).  
**Evidence:** [RELEASE_PACKAGE_02_B2_01_REPORT](reports/RELEASE_PACKAGE_02_B2_01_REPORT.md).

В рамках B2-01 не выполнять LR-14 unlink/relink, UI B2-03, миграции или новую permission-модель. Владелец отдельно указал, что фактические build/tests/browser проверки выполняет сам; в этой задаче они остаются **Not run**, а targeted tests добавляются как код.

### Required reading B2-01

После README → START_HERE → этого файла читать только:
1. `P0_CATALOG_PROCUREMENT_BOUNDARY.md`;
2. `STAGE-1_DATA_CONVENTIONS.md`;
3. относящиеся к audit/timeline/access положения `docs/04-foundation/FP-004_Сквозное_ERP-ядро_и_общие_бизнес-механизмы.md`;
4. текущий Gate и только затрагиваемый Procurement-код/тесты.

## Предыдущий Gate — B1-04, передача работы и проверка первого release package

**Прямой запрос владельца от 18 сентября 2026 года:** начать B1-04.  
**Ветка:** `codex/release-package-01`.  
**Исходный SHA B1-04:** `54fbb73a233bbfe6a085423f0ab5fd8668b916aa`.  
**Scope:** LR-03 + фактическая build/PostgreSQL regression-проверка B1-01–B1-04.  
**Gate:** [GATE-RELEASE-B1-04](GATE-RELEASE-B1-04.md).  
**Evidence:** [RELEASE_PACKAGE_01_REPORT](reports/RELEASE_PACKAGE_01_REPORT.md).

**Исполняемо проверенный кодовый SHA:** `59181519a89608e84d0c360ef8dae8c7708eefcc`.  
GitHub Actions run `35337618940`: **completed / success**. Locked restore — success; Release build — **0 warnings / 0 errors**; PostgreSQL 18 targeted suite — **40 passed / 0 failed / 0 skipped**. TRX artifact: `release-package-01-test-results` (artifact `10544155488`).

B1-04 implementation и backend/integration validation завершены. Browser/manual UI acceptance в этом Gate **не запускалась и остаётся отдельной приёмкой владельца**; независимый review итогового diff также ещё не выполнен. Поэтому пакет не объявляется полностью принятым и тем более production-ready. LR-23 остаётся открытым product decision. Main, production deploy и общая рабочая БД не изменялись.

## Предыдущий Gate — B1-03 (реализован и опубликован; исполнение проверяется в B1-04)

**Прямой запрос владельца от 18 сентября 2026 года:** выполнить B1-03 тем же пакетным режимом, пока владелец проводит приёмку B1-02.  
**Ветка:** `codex/release-package-01`.  
**Исходный commit B1-03:** `017136fb67f1e0b379a69251f97fd705032c93dc`.  
**Scope:** LR-02, LR-04; LR-23 фиксируется как открытый product decision без изменения эффективных прав.  
**Gate:** [GATE-RELEASE-B1-03](GATE-RELEASE-B1-03.md).  
**Evidence:** [RELEASE_PACKAGE_01_REPORT](reports/RELEASE_PACKAGE_01_REPORT.md).

B1-01 и B1-02 опубликованы, но исполнение всех трёх чат-задач проверяется в B1-04. B1-04 не начинать без отдельного запроса. Не запускать Server/Worker/Parser, не менять main и рабочую БД.

## Предыдущий Gate — B1-02 (реализован и опубликован; исполнение не проверено)

**Прямой запрос владельца от 18 сентября 2026 года:** выполнить B1-02 пакетно в режиме чата.  
**Ветка:** `codex/release-package-01`.  
**Исходно зафиксированный commit B1-02:** `14fa6064567fed4bb1a511ffc2c5576c82559dc2`.  
**Фактический parent публикации:** `9e1ffd5e23d176be253126b184bef3943851e99a`; появившийся параллельно analyzer-fix сохранён без переписывания истории.  
**Scope:** LR-01 / LR-05 — terminal `acquired` и сохранение исходного просроченного срока существующей проверки.  
**Gate:** [GATE-RELEASE-B1-02](GATE-RELEASE-B1-02.md).  
**План:** [RELEASE_PACKAGE_01_PLAN](RELEASE_PACKAGE_01_PLAN.md).  
**Evidence:** [RELEASE_PACKAGE_01_REPORT](reports/RELEASE_PACKAGE_01_REPORT.md).

B1-01 уже опубликован. Этот исторический блок описывает состояние на старте B1-02; актуальный статус находится выше. В текущем Gate не запускать build/tests/browser/Server/Worker и не менять main или БД.

### Required reading B1-02

После README → START_HERE → этого файла читать `RELEASE_PACKAGE_01_PLAN.md`, `GATE-RELEASE-B1-02.md`, относящиеся к acquisition/check инвариантам части Stage 1 и только затрагиваемый код/тесты.

## Предыдущий Gate — B1-01 (реализован и опубликован; исполнение не проверено)

**Прямой запрос владельца от 18 сентября 2026 года:** выполнить первую реализацию по согласованной схеме «3 задачи в чате → четвёртая задача и проверка пакета в режиме работы».  
**Ветка:** `codex/release-package-01`.  
**База B1-01:** `5b53c0b2f1dd3e12cfb9ec5721f38f590b69b16c`.  
**Scope:** LR-09 / LR-19 — результат сохранения, безопасный повтор трёх добавлений и диагностика.  
**Gate:** [GATE-RELEASE-B1-01](GATE-RELEASE-B1-01.md).  
**План:** [RELEASE_PACKAGE_01_PLAN](RELEASE_PACKAGE_01_PLAN.md).  
**Evidence и передача:** [RELEASE_PACKAGE_01_REPORT](reports/RELEASE_PACKAGE_01_REPORT.md).

Код и 27 тестов B1-01 входят в коммит первого добавления отчёта. После подтверждения удалённого ref статус — **реализовано и опубликовано; исполнение не проверено**. Restore/build/tests/browser/БД в чат-задаче не запускались и относятся к B1-04. Не объявлять этот этап принятым или production-ready только по наличию кода и тестов.

На момент закрытия B1-01 следующие задачи ещё не были начаты. Актуальный статус пакета указан в верхнем блоке этого файла. Main, merge/rebase/force-push, сброс/изменение рабочей БД, запуск Server/Worker/Parser и релиз не входят в текущее разрешение.

### Required reading B1-01

После README → START_HERE → этого файла читать:

1. `RELEASE_PACKAGE_01_PLAN.md`: границы пакета, B1-01, порядок 3+1.
2. `GATE-RELEASE-B1-01.md`: точные файлы, scope и условия приёмки.
3. `P0_CATALOG_PROCUREMENT_BOUNDARY.md`.
4. `STAGE-1_DATA_CONVENTIONS.md`.
5. `docs/04-foundation/FP-004_Сквозное_ERP-ядро_и_общие_бизнес-механизмы.md`: относящиеся к командам, истории, повтору и доступу правила.
6. Только затронутые исходники/тесты; при продолжении — отчёт пакета.

Ниже сохранён прежний текст как история. Его записи «активная работа» и старые ограничения этапов не активируют иные работы и не заменяют текущий Gate.

## Исторический контекст

> **Additive Collection V1 для Parser завершён:** [GATE-PARSER-COLLECTION-V1-ADDITIVE.md](GATE-PARSER-COLLECTION-V1-ADDITIVE.md),
> ветка `codex/parser-additive-contract`, база `ee8b5f3`. Добавлены partial outcome,
> coverage, machine reasons, сохранение полезных данных и возврат в auto-claim loop.
> Browser tests исключены. [Отчёт](reports/PARSER_COLLECTION_V1_ADDITIVE_REPORT.md).
> Ниже — завершённые этапы.

> **Завершён follow-up по надёжности результатов Collection:** ветка
> `codex/collectors-scheduling-ux`. Добавлены частичный результат, факты полноты,
> ограниченные серверные повторы, явное внимание оператора и сохранение данных при
> ошибочном завершении. Новая forward migration — двадцатая в цепочке. Release build
> и целевые небраузерные тесты пройдены; браузерные тесты по решению владельца не
> запускались. [Отчёт](reports/COLLECTION_RESULT_RECOVERY_REPORT.md).

> **Доработка по запросу владельца реализована:** [GATE-COLLECTORS-SCHEDULING-UX.md](GATE-COLLECTORS-SCHEDULING-UX.md), ветка `codex/collectors-scheduling-ux`. Исправления аудита расписаний и экрана Parser, Release build, 22/22 целевых теста; локальный Server + Worker запущены. [Отчёт](reports/COLLECTORS_SCHEDULING_UX_REPORT.md). Ожидается ручная визуальная приёмка. Браузерные тесты не запускались. Ниже — история предыдущих этапов.

> **Активная работа:** [GATE-INTEGRATE-PARSER-UI-STORAGE.md](GATE-INTEGRATE-PARSER-UI-STORAGE.md), ветка `codex/integrate-parser-ui-storage`. Владелец 17 сентября 2026 года явно разрешил локальные коммиты и слияние завершённых Parser/UI, Яндекс Диска и исправления прав документов. Объединение и 116 целевых тестов завершены; Server запущен на https://localhost:7240 для ручной приёмки. Evidence: [INTEGRATED_PARSER_UI_STORAGE_REPORT.md](reports/INTEGRATED_PARSER_UI_STORAGE_REPORT.md). Required reading и проверки — в Gate. Main, push и production apply не входят. Следующие записи — исторический контекст.

> **Яндекс Диск:** [GATE-YANDEX-DISK-STORAGE.md](GATE-YANDEX-DISK-STORAGE.md), реализован и проверен: [отчёт](reports/YANDEX_DISK_STORAGE_REPORT.md), 13 целевых тестов включая реальный Диск.

> **Активная работа:** [GATE-PARSER-WORKSPACE-UX.md](GATE-PARSER-WORKSPACE-UX.md), ветка `codex/parser-workspace-ux`. Переработка Parser разрешена владельцем после аудита. Реализация и проверки завершены: [отчёт Parser UX](reports/PARSER_WORKSPACE_UX_REPORT.md), 89 offline/WPF и 5 PostgreSQL тестов. Ожидается пользовательская приёмка живого сценария. Изменения Server UI сохраняются; следующий блок — предыдущий Gate.


> **Активный Gate от 17 сентября 2026 года:**
> [`GATE-UI-CONSISTENCY.md`](GATE-UI-CONSISTENCY.md) — визуальная унификация
> существующих страниц; ветка `codex/ui-consistency`. Реализация завершена,
> ожидает визуальной приёмки владельца. Evidence:
> [`UI_CONSISTENCY_REPORT.md`](reports/UI_CONSISTENCY_REPORT.md).
> Required reading и scope определены этим Gate. Browser/visual
> tests не запускались: визуальную приёмку выполнит владелец. Следующие записи —
> исторический контекст, они не активируют другую работу.

> **Завершённый Gate от 17 сентября 2026 года по прямому запросу владельца:**
> [`GATE-COLLECTORS-V1-3-UI.md`](GATE-COLLECTORS-V1-3-UI.md) — законченный экран
> `/collectors` по макету «Поиски и парсинг v1.3». Ветка
> `codex/collectors-v1-3-ui`, база — проверенный интеграционный HEAD `5b254b9`.
> Server остаётся authority; Parser получает право создавать группы и поиски только
> при включённом `CanManageSearches`. Страницы закупки и ParserSpike не изменять.
> Browser/visual tests не выполнялись по решению владельца. Реализация и проверки
> завершены; владелец разрешил приёмку и локальное слияние 17 сентября 2026 года.
> Ручная визуальная проверка остаётся рекомендуемым следующим шагом. Evidence:
> [`COLLECTORS_V1_3_UI_REPORT.md`](reports/COLLECTORS_V1_3_UI_REPORT.md).
> Интеграция слита в локальный `main`; push не выполнялся.

> Предыдущая интеграция Collector Server Management + Universal Parser +
> PropertyCase V2 завершена в `5b254b9`; evidence:
> [`INTEGRATED_SERVER_PROPERTY_V2_REPORT.md`](reports/INTEGRATED_SERVER_PROPERTY_V2_REPORT.md).

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
