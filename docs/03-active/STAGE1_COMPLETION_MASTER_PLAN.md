# Stage 1 — Completion Master Plan

**Статус:** canonical execution plan — review corrections applied  
**Дата ревизии:** 2026-09-15  
**Рабочая ветка:** `codex/stage-1-procurement-core`  
**Baseline master-plan:** `733b7e49fb878fd4d633942ececf4272f177509a`  
**Independent review:** `docs/03-active/STAGE1_COMPLETION_MASTER_PLAN_REVIEW.md` @ `298cb4c28ac2df1cd929f171c5c84e988e3fc35a`  
**Production baseline для уже выполненного Stage 1:** `b6d64e6258bf23d7263695080ff588afe3c04eae`  
**Назначение:** единственный execution-документ для последовательного завершения Stage 1 без повторного архитектурного исследования.

---

## 1. Как пользоваться этим документом

Этот документ заменяет роль разрозненных task notes как **главный execution-документ завершения Stage 1**. Следующий implementation-agent начинает с **Phase 1** и не возвращается к повторному проектированию уже принятых границ.

При конфликте источников использовать такой приоритет:

1. `docs/03-active/P0_CATALOG_PROCUREMENT_BOUNDARY.md` и утверждённые screen specs `docs/14-ui-kit/screens/01..10`;
2. этот master plan;
3. актуальные ADR / foundation / FP-документы, если они не противоречат п.1–2;
4. старые `ACTIVE_TASK.md`, `README.md`, `START_HERE.md`, старые FP-допущения и текущая production-реализация как описание **CURRENT**, а не как целевая архитектура.

HTML prototypes в `docs/14-ui-kit/prototypes/` задают композицию и UX-reference. Их demo data, JS и технические названия не являются production contract. Реализация остаётся на существующем Blazor/component/token stack.

### 1.1. Жёсткие границы программы работ

- **не читать глубоко и не менять локальный Parser/Collector Agent** (`LandErp.ParserSpike*`) в Stage 1 completion;
- Phase 2 меняет серверный claim/routing mechanism и сохраняет V1 wire contract/endpoints настолько, насколько это возможно без изменения локального Parser Agent;
- не реализовывать `Owned Assets`, `LandAsset`, Investor cabinet и полноценный post-purchase contour;
- Stage 1 заканчивается подтверждённым `Acquired` внутри `PropertyCase`: фактическая цена + дата + комментарий; дальнейший `LandAsset` — только будущая точка передачи;
- не создавать отдельный большой модуль/экран «Сделка»;
- старые migrations не переписывать: только новые additive/transform/cleanup migrations;
- не делать глобальный UI rewrite отдельно от нужного business flow;
- не строить generic workflow engine, generic media/document platform, distributed offline sync framework или event-sourcing rewrite ради Stage 1.

### 1.2. Сквозное правило scope / permissions

**Security correctness не откладывается до Phase 6.** Каждый новый read model, command, endpoint и UI action в каждой фазе обязан в той же фазе получить server-side permission/access predicate и соответствующие positive/negative tests на текущем `AccessContext`.

Канонические Stage 1 semantics:

- **Catalog / Incoming** — общий organization-shared intake pool; Search/Agent/Department/Team не определяют ownership входящего элемента;
- **Procurement** — `Own / AssignedObjects / Team / Department / Organization` вычисляются через `PropertyCase` responsibility/assignment и текущие employee organization assignments, **никогда через Listing metadata**;
- **Collection management** — organization-admin surface; более узкие scopes не получают частично отфильтрованный административный экран;
- **Organization administration** — mutations требуют organization-admin capability; read views соблюдают разрешённый scope;
- **Audit** — `audit.read` + organization-level visibility по approved screen;
- **Overview** — использует те же scope-aware predicates/query services, что Catalog/Procurement/Collection; не изобретает собственную трактовку scope.

Phase 6 остаётся фазой Organization/Identity UX и финальной cross-module permission-matrix проверки, а не первой фазой, где access становится корректным.

---

## 2. Канонические доменные границы

### 2.1. Catalog / incoming

**CURRENT**

- `Catalog.Domain.Listing` фактически является marketplace listing;
- source classification зависит от `LandErp.Collector.Contracts.V1.ListingSource`;
- `ExternalId` и `Url` обязательны;
- `DepartmentId`/`TeamId` приходят из `SearchConfiguration`;
- manual/Telegram/referral ingestion без fake Agent/Job отсутствует;
- `/procurement` начинает выборку от Listings.

**TARGET**

- нейтральный `CatalogItem` concept для Avito/Cian/Telegram/manual/referral/agent/other;
- server-owned source code; Collector enum маппится только на ingestion boundary;
- `ExternalId?`, `Url?` optional; `null` означает отсутствие external identity, пустая строка не используется как fake id;
- uniqueness внешней identity применяется только когда external identity существует;
- automatic observations остаются историей Collector; manual/Telegram-like записи имеют нормальный provenance без fake Job/Observation;
- Catalog — общий входящий pool организации;
- действия входящего слоя: отклонить, наблюдать/мониторить, связать с существующим case, `Взять в работу`.

Физическое переименование таблицы/класса `Listing` можно отложить, если это снижает migration risk. Архитектурную независимость Catalog от Collector enum и Procurement от Listing откладывать нельзя.

### 2.2. PropertyCase / Procurement

**TARGET**

- `PropertyCase` — самостоятельный Procurement aggregate/root;
- `0..N` связанных Catalog items через подтверждённую source-link relation;
- один PropertyCase может иметь много источников;
- **один подтверждённый Catalog item не может одновременно принадлежать двум PropertyCase**;
- possible match / duplicate candidate не является второй confirmed link;
- после boundary command вся Procurement работа адресуется `CaseId`;
- queue начинается с `PropertyCases`;
- case содержит собственные verified/working facts; source data остаются provenance;
- удаление, закрытие или изменение внешнего объявления не удаляет source history и не разрушает case;
- Telegram + Avito + Cian могут относиться к одному case.

Hard DB invariant для confirmed source links:

- unique constraint/index гарантирует `CatalogItemId -> at most one confirmed PropertyCase`;
- `TakeToWork`/confirmed linking выполняются transactionally;
- конкурентные запросы двух менеджеров не могут создать два confirmed case для одного item;
- если confirmed link уже существует, повторная/проигравшая команда возвращает/open существующий Case, а не создаёт второй.

### 2.3. Collection server

Целевая цепочка:

`Search -> Schedule -> Pending Job -> shared pool -> compatible free Agent -> Claim/Lease -> Catalog`.

- Search не содержит обязательный Agent/Department/Team routing;
- SearchGroup — тонкая организационная группировка, не новый generic reference-data framework;
- job получает actual executor при claim/lease, не при enqueue;
- capability/source compatibility проверяется сервером;
- results идут в organization-wide Catalog;
- V1 Collector claim/heartbeat/result contract сохраняется совместимым настолько, насколько это возможно;
- локальный Parser Agent не читается глубоко и не изменяется.

### 2.4. Procurement business flow

Целевая цепочка:

`входящий item -> первичный отбор -> взять в работу / связать -> PropertyCase -> первичный анализ -> manager/head handoff -> переговоры -> quick/deep checks -> site inspection -> решение -> Acquired`.

Карточка case: `Основное / Переговоры / Проверки / Осмотр / Документы / История / Источники`.

### 2.5. Lifecycle reactivation

Канонический сценарий:

`отклонили -> поставили на мониторинг -> условие выполнено -> снова интересно`.

Правило идентичности:

- если источник ещё не был связан с case — новый интерес может привести к обычному `TakeToWork`;
- если для этого реального объекта уже существует PropertyCase и источник/confirmed match указывает на него — система выполняет **resume/reopen существующего Case**, а не создаёт второй;
- head return -> manager rework -> resubmit всегда остаётся тем же PropertyCase;
- предотвращение дублей опирается на confirmed-link unique DB invariant + transactionally idempotent commands + явный link/resume path; possible matches сами по себе не создают confirmed ownership.

### 2.6. Attachments / storage boundary Stage 1

Phases 4–5 используют минимальную общую boundary, а не отдельный большой media framework.

Attachment должен поддерживать:

- фото;
- документы;
- видео;
- аудио, когда это требуется approved flow;
- внешние ссылки/metadata;
- ownership к `Case`, `Inspection`, `InspectionItem`, `Negotiation` и/или `Check`;
- Organization ownership и authorization через owning business object;
- stable reference + upload/read metadata; raw storage identifiers/secrets не показываются staff UI;
- retry/not-yet-synchronized state для media при временном сетевом сбое.

### 2.7. Organization / Identity, Audit, Overview

Сохраняются принятые границы:

- `Организация`: `Структура / Сотрудники / Должности`, rename/archive/restore, direct login/password creation primary, invitation secondary, position != permissions;
- Audit — immutable current store + semantic read projection/formatters; не event-sourcing rewrite;
- Overview — query-time aggregates/read models first; materialization только после измеренной необходимости.

---

## 3. Superseded assumptions register

| Старое предположение | Канонический target |
|---|---|
| Search закреплён за конкретным Agent | shared pool + capability-based claim |
| Search маршрутизирует данные через Department/Team | results организации идут в общий Catalog |
| `/procurement` одновременно входящие и закупка | отдельные Incoming Catalog и PropertyCase queue |
| PropertyCase существует только из Listing | PropertyCase независим, `0..N` source links |
| Procurement commands адресуются ListingId | после take-work только CaseId |
| Catalog source enum принадлежит Collector contract | server-owned Catalog source code |
| ручные данные имитируют Collector result | отдельный manual ingestion/provenance path |
| monitored/rejected source всегда создаёт новый case при повторном интересе | существующий case resume/reopen, если это тот же объект |
| scope Procurement выводится из Listing Department/Team | scope выводится из Case responsibility/assignment |
| Employees и Org structure — разные top-level разделы | один `Организация` с tabs |
| invitation — единственный/главный employee flow | direct login/password creation primary, invitation secondary |
| Audit показывает raw storage fields | semantic audit read model + hidden technical details |
| Home — набор ссылок | operational Overview |
| покупка автоматически создаёт InvestmentProject | Stage 1 заканчивается Acquired; LandAsset/Projects позже |

---

## 4. Target server contracts

### 4.1. Catalog

Минимально нужны:

- `ListIncomingCatalog(...)`;
- `GetCatalogItem(id)`;
- `CreateManualCatalogItem(...)`;
- `DismissCatalogItem(...)`;
- `SetCatalogMonitoring(...)`;
- `TakeCatalogItemToWork(catalogItemId, createNewCase | existingCaseId)`;
- `LinkCatalogItemToCase(catalogItemId, caseId, relationType)`;
- `ResumeExistingCaseFromCatalogItem(...)`;
- source/provenance read model.

### 4.2. Procurement

- `ListPropertyCases(...)`;
- `GetPropertyCase(caseId)`;
- workflow / transfer / notes / negotiations / checks / inspection / purchase — по `CaseId`;
- source changes создают attention/discrepancy signal и не перезаписывают case facts автоматически.

### 4.3. Temporary ListingId -> CaseId compatibility

Новый canonical route — CaseId-based, например `/procurement/{caseId:guid}`.

Текущий route `/procurement/listings/{ListingId}` временно сохраняется как server-side compatibility adapter:

1. найти confirmed связь `Listing/CatalogItem -> PropertyCase`;
2. redirect/forward на canonical Case route;
3. **не создавать PropertyCase неявно**, если link отсутствует;
4. ListingId не используется как primary identity новых business operations;
5. legacy contracts/routes удаляются только в **Phase 9** после доказанного cutover всех server UI/tests/known consumers.

### 4.4. Collection

- Search definition/revision/config;
- SearchGroup;
- schedule;
- enqueue/manual run;
- pending shared jobs;
- compatible claim/lease;
- actual AgentId как executor после claim;
- job/history read model;
- V1 Collector endpoints compatible до отдельной Parser Agent phase.

---

# 5. Implementation phases

Всего: **9 phases**. Число фаз review не меняет. Phase 2 остаётся одной numbered phase, но имеет обязательные checkpoint **2A** и **2B**.

---

## Phase 1 — P0: полностью закрыть Catalog / Procurement boundary

**Цель**  
Полностью выполнить утверждённый `P0_CATALOG_PROCUREMENT_BOUNDARY.md`: доказать, что Catalog универсален, PropertyCase самостоятельный, Procurement case-centric, а manual/Telegram-like ingress работает без Collector сущностей.

### Обязательный scope Phase 1

1. **Independent PropertyCase**
   - убрать business dependency от обязательного `PropertyCase.ListingId`;
   - case может существовать/read при `0..N` source links;
   - добавить минимальные case-owned verified/working facts, не копируя весь source payload.

2. **Confirmed source links + hard uniqueness**
   - ввести `PropertyCaseCatalogItem` / `PropertyCaseSourceLink`;
   - confirmed `CatalogItemId` имеет DB uniqueness: at most one PropertyCase;
   - possible duplicate/match хранится отдельно/неподтверждённо и не нарушает invariant.

3. **Server-owned Catalog source model**
   - server-owned source code abstraction;
   - Collector `ListingSource` маппится на boundary;
   - `ExternalId?`, `Url?` nullable для non-marketplace;
   - никакого `""` как fake external id; conditional uniqueness только для реальной external identity.

4. **Minimal manual / Telegram-like ingestion**
   - server command `CreateManualCatalogItem` с source/provenance actor/time/comment;
   - минимальный UI/server path `+ Добавить объявление / предложение`;
   - manual/Telegram-like item не создаёт fake Agent/Job/Observation.

5. **Idempotent/concurrent-safe `TakeToWork`**
   - command работает по CatalogItemId;
   - create new Case или explicit link to existing Case;
   - transaction + DB uniqueness защищают от двух конкурентных менеджеров;
   - повтор возвращает/open existing Case;
   - assignment/task + business timeline + audit создаются по действующим правилам без дублей.

6. **CaseId cutover Procurement**
   - `ProcurementWorkspace` и queue начинаются с PropertyCases;
   - card/decision/note/forward/return/approval contracts — `CaseId`;
   - Listing/CatalogItem id остаётся только у incoming/source-link actions.

7. **Scope-safe Procurement**
   - Department/Team visibility не вычисляется через Listing;
   - access predicate использует organization + Case responsibility/assignment + employee organization assignment + `AccessContext`;
   - новые queries/commands получают positive + negative scope tests в этой фазе.

8. **Temporary legacy compatibility**
   - `/procurement/listings/{ListingId}` resolves existing confirmed link и redirects/forwards на Case route;
   - no linked Case => no implicit creation;
   - старые ListingId contracts не расширяются и остаются deprecated compatibility-only до Phase 9.

### Миграции Phase 1

Порядок обязателен:

1. **Expand** — source-link table, nullable/new case-owned columns, server source classification support, optional external identity support;
2. **Backfill** — для каждого legacy `PropertyCase.ListingId` создать confirmed source link;
3. backfill минимальных case-owned facts из source snapshot с **migration/system provenance**, не создавая fake human confirmation;
4. сохранить BusinessNumber, assignment, task, approvals, transitions, timeline, audit, Listing/Observation history;
5. **Cutover** reads/writes/routes на CaseId;
6. снять required FK/unique `PropertyCase.ListingId` coupling после green verification; сам legacy field/adapter может остаться до Phase 9;
7. source removal не hard-delete: сохранять last known snapshot/observations/link provenance и unavailable/removed-at-source classification.

### UI Phase 1

Минимальная адаптация, достаточная для P0 acceptance:

- manual/Telegram-like create;
- минимальный incoming action `Взять в работу` / link existing;
- case-based queue/card route;
- без полного Incoming screen/filter/monitoring UX — это Phase 3.

### Tests / executable verification Phase 1

Обязательные automated scenarios:

- PropertyCase read/write без Listing;
- one Case -> 3 confirmed sources;
- one Catalog item -> second confirmed Case link rejected;
- concurrent `TakeToWork` двух менеджеров -> ровно один Case/confirmed link;
- repeated `TakeToWork` -> existing Case;
- manual item без URL/ExternalId создаётся без fake Collector entities;
- manual/Telegram-like -> take to work -> CaseId flow;
- existing Avito flow остаётся green;
- source disappearance/change не ломает Case и не overwrite verified facts;
- scope Team/Department/Own/Assigned/Organization идёт через Case responsibility, не Listing metadata;
- legacy ListingId route redirects existing link и не creates Case;
- migration from legacy Stage 1 dataset сохраняет business history и migration provenance.

Executable baseline:

```powershell
dotnet restore LandErp.slnx --locked-mode
dotnet build LandErp.slnx --no-restore
dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj --no-build
```

PostgreSQL migration tests: clean DB + legacy Stage 1 DB; `Database.HasPendingModelChanges() == false`.

### Exit gate Phase 1

Перед Phase 2 **обязательно истинно**:

- весь P0 Catalog/Procurement boundary закрыт, включая manual/Telegram-like ingress;
- PropertyCase независим и поддерживает `0..N` sources;
- confirmed source uniqueness обеспечена DB + transaction;
- Procurement reads/commands/route canonical = CaseId;
- legacy ListingId route compatibility доказана;
- Avito и manual/Telegram-like acceptance scenarios green;
- scope-safe Procurement доказан без Listing Department/Team;
- migrations воспроизводимы и не теряют history/provenance.

**Phase 1 READY TO IMPLEMENT**

---

## Phase 2 — P0: Collection shared pool и server-owned routing

**Цель**  
Перевести Collection с preassigned Agent/Search routing на shared server pool, не меняя локальный Parser Agent и не ломая V1 wire contract.

Phase 2 остаётся **одной фазой**, но выполнять её строго в порядке 2A -> 2B.

### Phase 2A — shared pool migration / cutover

**Сначала только migration-critical foundation. 2B не начинать до green 2A.**

Конкретно:

1. Search перестаёт требовать AgentId/DepartmentId/TeamId для новых writes;
2. Pending job может иметь `AgentId = null`; executor устанавливается атомарно при Claim;
3. `ClaimAsync` выбирает compatible pending job организации (`FOR UPDATE SKIP LOCKED` или эквивалентно безопасный mechanism);
4. heartbeat/result paths после claim продолжают валидировать `JobId + Agent + LeaseId`;
5. ingestion больше не копирует Search Department/Team в Catalog ownership;
6. server maps Collector V1 source -> server-owned Catalog source code;
7. Parser Agent internals не читать глубоко и не менять.

#### Deterministic migration старых jobs

- **Pending** -> legacy preassignment освобождается; job входит в shared pool;
- **Leased с валидным незавершённым lease** -> не отбирать и не reassign; дождаться completion/expiry;
- **expired unfinished lease** -> reclaim может выполнить другой compatible Agent;
- **Completed** -> навсегда сохранить фактического Agent как historical executor;
- terminal `LimitReached`, `AwaitingManualAction`, `Failed`, `Interrupted` -> сохранить текущего Agent как historical executor, если он отражает фактическое выполнение;
- job executor nullable только для unclaimed work; claim заполняет его атомарно;
- legacy Search `AgentId/DepartmentId/TeamId` columns физически не удалять, пока новый код и compatibility path их больше не читают.

#### Tests 2A

- old Pending освобождается в pool;
- active Leased не steals;
- expired lease reclaimable другим compatible Agent;
- Completed сохраняет historical Agent;
- two Agents cannot lease same Job;
- incompatible Agent не получает Job;
- V1 registration/heartbeat/claim/result remains green;
- Catalog ingestion organization-wide и не получает Search Department/Team ownership.

**2A exit gate:** deterministic migration + shared claim + V1 compatibility + concurrency tests green.

### Phase 2B — SearchGroup + schedules + management UI

Только после green 2A:

- SearchGroup — минимальная thin grouping model для 30–50+ searches; не generic dictionary framework;
- typed server schedules;
- scheduler создаёт idempotent jobs per due run;
- manual `Run now` остаётся;
- history/counters/read model;
- approved `Сбор данных` UI: groups, searches, schedules, shared queue, history, parsers;
- human-readable statuses; raw technical enum/class names не являются staff vocabulary.

Scope: Collection management — organization-admin surface; server-side authorization и negative tests входят в Phase 2.

### Exit gate Phase 2

Перед Phase 3:

- Search не закреплён за машиной/Department/Team;
- Pending jobs работают через shared pool;
- active leases/history мигрированы безопасно;
- compatible Agent определяется при claim;
- schedules/group/UI строятся уже поверх 2A model;
- V1 Collector wire path green без code change локального Parser Agent;
- новый incoming поток не получает routing ownership от search/agent.

---

## Phase 3 — P1: полный Incoming Catalog + отдельная Procurement queue

**Цель**  
Довести полный approved Incoming UX/read model поверх уже готовых Phase 1 boundary commands и Phase 2 ingestion.

### Конкретные изменения

- полноценный incoming list/detail drawer: server paging/filter/search/source/status/age/price/area;
- statuses/actions без generic workflow engine;
- `CreateManualCatalogItem`, `TakeToWork`, link-existing из Phase 1 **переиспользуются**, не дублируются;
- Procurement queue показывает только PropertyCases и case-specific next action/responsibility/source summary;
- navigation: отдельные `Входящие` и `Закупка`.

### Мониторинг цены

Для monitoring хранить независимо:

- целевую общую цену;
- целевую цену за сотку;
- правило OR/AND только если это явно требуется approved screen; без лишней rule-engine абстракции;
- текущие observed/source values и время последней оценки.

Когда заданное условие выполнено:

- item автоматически возвращается в активные входящие/attention;
- создаётся понятный attention signal/event;
- verified Case facts не меняются автоматически.

Классификации **не смешивать** в один общий rejected-status:

- `Duplicate`;
- `Fake`;
- `Removed` / unavailable-at-source;
- `Sold`;
- обычное business dismissal/rejection;
- monitoring.

Source removal сохраняет provenance/snapshots/observations, а не hard-deletes history.

### Reactivation / duplicate prevention

Lifecycle:

`отклонили -> мониторинг -> условие выполнено -> снова интересно`.

- если source связан с существующим rejected/paused Case, пользователь выполняет explicit **resume/reopen existing Case**;
- система не создаёт второй Case для того же confirmed source/real object;
- unique confirmed-link constraint + transactional `TakeToWork`/resume command являются последней защитой от дубля;
- source revision может вернуть item в attention, но не переписать verified facts.

Scope: Incoming — organization-shared pool с permission predicate; Procurement queue использует Case scope rules из Phase 1.

### Tests / exit gate Phase 3

- manual item без URL/ExternalId проходит full Incoming UX;
- target total price triggers reactivation;
- target price-per-sotka triggers reactivation;
- Duplicate/Fake/Removed/Sold остаются различимыми;
- monitored source -> condition met -> active attention;
- linked rejected/paused Case -> resume same Case;
- source change -> attention, no verified overwrite;
- Avito + Telegram + Cian -> one Case;
- no cross-organization data leakage.

Перед Phase 4 все approved incoming paths приводят к единому case-centric Procurement flow.

---

## Phase 4 — P1: PropertyCase dossier, workflow, переговоры и проверки

**Цель**  
Довести карточку объекта закупки до рабочего инструмента менеджера и руководителя.

### Attachment foundation в начале Phase 4

До Negotiations/Checks/Documents создать минимальную attachment/storage boundary из §2.6:

- stable attachment reference + metadata;
- Organization + owning business object authorization;
- фото/документы/видео/аудио/ссылки;
- ownership к Case/Negotiation/Check (Inspection подключится в Phase 5);
- никакой отдельной универсальной document-management/media platform.

### Business scope

- tabs `Основное / Переговоры / Проверки / Осмотр / Документы / История / Источники`;
- manager -> head -> return -> rework -> resubmit -> decision в одном Case;
- rejected/paused Case может быть explicit resume/reopen по lifecycle Phase 3, с сохранением prior decisions/history;
- case-owned verified facts + source discrepancies отдельно;
- Negotiation: Ask / SellerOffer / BuyerOffer / Agreed, conditions, contact/channel, next step;
- Quick checks — structured primary screening;
- Deep/legal checks — structured records/blockers/results, без technical `DD` в staff UI;
- documents/attachments привязаны к owning Case/Negotiation/Check;
- history/timeline — business events, не замена system audit;
- source values применяются к verified case fields только explicit user action.

Scope/permissions реализуются вместе с каждым command/read model этой фазы.

### Tests / exit gate Phase 4

- negotiation price types не overwrite друг друга;
- agreed != acquired;
- return/rework/resubmit сохраняет history/tasks;
- reopen существующего Case не создаёт duplicate;
- attachment access следует owning Case permissions;
- blockers permission-protected;
- source discrepancy не mutate verified field implicit;
- manager/head/team/department/organization scope green.

Перед Phase 5 Case содержит достаточно структурированного состояния для объяснимого purchase decision.

---

## Phase 5 — P1: Site Inspection + purchase completion to Acquired

**Цель**  
Закрыть последний business gap полного Stage 1 procurement cycle.

### Site Inspection

- server-configurable/versioned inspection template/checklist;
- template designer / universal rules engine в Stage 1 не нужен;
- inspection instance per Case: item result/status/note/media, progress, overall conclusion;
- attachment boundary Phase 4 расширяется ownership к Inspection/InspectionItem;
- mobile-first screen по `04-site-inspection.md`.

### Минимальное offline-draft поведение

Полноценный distributed sync/conflict framework **не строить**.

Stage 1 обязан обеспечить:

- пользователь может продолжить заполнять **уже начатый** осмотр при кратком отсутствии связи;
- локальный draft answers/notes сохраняется и переживает временный disconnect/reload в рамках выбранной client storage strategy;
- после восстановления связи выполняется безопасное сохранение с concurrency/version check;
- при конфликте данные не молча перезаписываются: пользователь получает recoverable error/merge-by-retry path;
- media может иметь явное состояние `not yet synchronized / retry upload`; full offline media sync не требуется.

### Acquired

- `Отметить как куплено`: actual price, acquisition date, comment + разумная validation;
- command idempotent/terminal: Acquired нельзя выполнить дважды;
- закрывает active procurement work по согласованному правилу;
- timeline + audit event;
- PropertyCase остаётся readable;
- LandAsset/InvestmentProject не создаются.

Scope/permissions для inspection/media/acquisition реализуются в Phase 5.

### Exit gate Phase 5

- mobile inspection works online и выдерживает краткий disconnect через local draft;
- safe reconnect save доказан;
- media retry state не теряет references;
- `incoming -> case -> checks/negotiation -> inspection -> acquired` green;
- Acquired terminal/idempotent;
- полный Stage 1 business core закончен без post-purchase modules.

---

## Phase 6 — P2: Organization, employee creation, permissions и scope UX

**Цель**  
Довести административный UX и подтвердить общую permission matrix. Эта фаза **не вводит впервые** security semantics предыдущих модулей.

### Конкретные изменения

- объединить `/organization` и `/employees` в `Организация` с tabs `Структура / Сотрудники / Должности`;
- Department -> Team -> Employees;
- rename/archive/restore Department/Team/Position; employee disable/archive/restore; hard delete не предоставлять;
- direct account creation: login + generated temporary password, one-time display;
- first login requires password change по Identity policy;
- invitation остаётся secondary action;
- position != permission role;
- scope editor: `Свои`, `Назначенные объекты`, `Команда`, `Отдел`, `Вся организация`;
- navigation visibility соответствует capability, но не заменяет server authorization;
- финально проверить permission matrix Catalog/Procurement/Inspection/Collection/Overview/Audit.

### Exit gate Phase 6

- employee create -> login -> forced change -> work in permitted scope;
- archived employee не работает, history сохраняется;
- position change не меняет permissions скрыто;
- каждая предыдущая фаза имеет собственные scope tests; Phase 6 cross-check не обнаруживает module-specific bypasses.

---

## Phase 7 — P2: semantic Audit + очистка technical vocabulary

**Цель**  
Сделать аудит понятным руководителю/администратору без перестройки audit storage.

- immutable current audit store сохраняется;
- server paged/filterable semantic read model;
- formatters per action family;
- actor/target display identity + human-readable before/after;
- technical details drawer: raw action/entity/GUID/correlation/JSON только разрешённым пользователям;
- archived entities остаются понятными за счёт достаточного non-secret display snapshot metadata для новых events;
- unknown legacy action имеет safe fallback;
- UI grep/pass по raw enum/class/GUID leakage.

Scope: `audit.read` + organization-level visibility enforced server-side.

**Exit gate:** audit paging/filtering/tenant isolation/technical-details authorization green; event-sourcing rewrite отсутствует.

---

## Phase 8 — P2: operational Overview

**Цель**  
Сделать `/` ежедневной рабочей точкой входа.

- один bounded server Overview query/read service;
- metrics: new incoming, in procurement, attention, waiting decision;
- attention feed + reason/age/link;
- procurement now by stage/next action;
- `Моя работа` из assignments/tasks/notifications;
- Collection health без raw statuses;
- permission-aware quick actions;
- team block только при разрешённом scope;
- counts/rows переиспользуют module predicates/query services и не реализуют scope заново;
- materialized projections только при измеренной необходимости.

**Exit gate:** Overview counts совпадают с source module queries при том же `AccessContext`; foreign organization data invisible.

---

## Phase 9 — P2/P3: Stage 1 hardening, migration cleanup и completion gate

**Цель**  
Удалить только доказанно ненужные compatibility хвосты и завершить Stage 1.

### Destructive cleanup gates

Удалять legacy элементы можно только если доказано всё соответствующее:

1. все server UI/tests/known Procurement consumers используют CaseId;
2. `/procurement/listings/{ListingId}` нужен только как tested deprecated adapter либо уже не имеет known consumers;
3. no remaining application read/write зависит от `PropertyCase.ListingId`;
4. no remaining Collection logic читает/пишет legacy Search Agent/Department/Team routing;
5. deterministic migration verification green на clean + legacy datasets;
6. V1 Collector server compatibility green;
7. completed/terminal Job executor history сохранена;
8. no pending model changes.

Только после этого:

- удалить obsolete ListingId Procurement adapters/contracts/field;
- удалить legacy Search routing columns;
- cleanup obsolete routes/components/read models;
- обновить `ACTIVE_TASK.md`, `README.md`, `START_HERE.md`, relevant FP status/notes;
- проверить indexes/query plans incoming/case queue/audit/overview;
- error/concurrency/empty/loading states;
- accessibility/responsive pass;
- final Stage 1 report с migration/verification evidence.

**Parser Agent:** локальный Agent по-прежнему не рефакторить. V1 server compatibility не удалять до отдельной Parser phase, если реальный Agent всё ещё её использует.

### Exit gate Stage 1

- clean build/tests;
- clean + legacy migration tests;
- restart persistence;
- authorization negative tests;
- idempotency/concurrency tests;
- all E2E scenarios green;
- docs match implementation;
- no mandatory legacy couplings from superseded register;
- Stage 1 usable by real staff without manual DB intervention.

---

# 6. Сквозная verification matrix

Каждый scenario получает automated integration coverage, где разумно, плюс executable/browser smoke для critical user paths.

| Scenario / invariant | Фаза |
|---|---|
| Avito -> Incoming -> TakeToWork -> PropertyCase -> Acquired | 1–5 |
| Manual/Telegram item без URL/ExternalId -> same flow | 1, 3–5 |
| one Case -> Telegram + Avito + Cian sources | 1/3/4 |
| one Catalog item -> at most one confirmed Case | 1 |
| concurrent TakeToWork -> one Case/link | 1 |
| repeated TakeToWork -> existing Case | 1 |
| legacy ListingId route -> existing Case redirect, no implicit create | 1/9 |
| external source removed -> provenance/history retained, Case alive | 1/3 |
| source changed -> attention, no verified overwrite | 1/3/4 |
| migration backfill preserves history + system provenance | 1 |
| rejected -> monitoring -> threshold met -> active incoming | 3 |
| target total price trigger | 3 |
| target price-per-sotka trigger | 3 |
| Duplicate/Fake/Removed/Sold distinct | 3 |
| existing rejected/paused Case -> resume/reopen same Case | 3/4 |
| head return -> manager rework -> resubmit same Case | 4 |
| attachment access follows owning Case/Negotiation/Check | 4 |
| Site Inspection local draft survives short disconnect | 5 |
| reconnect saves draft safely; conflict not silent | 5 |
| media retry state preserves reference | 5 |
| Acquired cannot happen twice | 5 |
| create employee -> temp password -> forced change -> scoped work | 6 |
| Pending legacy Job released to shared pool | 2A |
| active Leased Job not stolen | 2A |
| expired lease reclaimable by different compatible Agent | 2A |
| Completed/terminal Job preserves historical Agent | 2A |
| two Agents cannot lease same Job | 2A |
| schedule -> shared Job -> compatible claim -> Catalog | 2B/3 |
| current V1 Collector registration/heartbeat/claim/result green | 2A/9 |
| Audit semantic row + technical details permission | 7 |
| Overview counters equal module queries under same scope | 8 |
| old/foreign organization cannot read case/catalog/audit/overview | every relevant phase |
| archived employee/unit/position historical references remain | 6/7 |
| duplicate Collector delivery remains idempotent | 2/3 |
| audit/timeline/observations remain append-only where declared | all relevant |

---

# 7. Migration strategy как единая программа

Каждая breaking boundary проходит одинаковую последовательность:

1. **Expand** — new tables/nullable columns/read-write structures;
2. **Backfill** — deterministic data migration + provenance;
3. **Dual compatibility** — legacy reads/adapters работают, новые commands уже target-centric;
4. **Cutover** — CaseId/shared pool/server source code;
5. **Verify** — clean + legacy migration tests, counts/invariants/concurrency;
6. **Contract** — destructive cleanup только в доказанно безопасной последующей migration, главным образом Phase 9.

Нельзя:

- редактировать старые migration files;
- пересоздавать Listing/Observation history;
- менять BusinessNumber существующих Cases;
- лечить миграцию удалением production-like DB;
- создавать fake Agent/Job/Observation для manual Catalog items;
- hard-delete исчезнувший source provenance;
- очищать active lease/executor history ради новой shared-pool модели.

---

# 8. Test architecture после cutover

Минимальные server integration suites:

1. **CatalogBoundaryTests** — manual/Telegram, source ownership, confirmed uniqueness, concurrent take/link, multi-source, source changes, compatibility route;
2. **ProcurementWorkflowTests** — CaseId transitions, assignments, approvals, scope, concurrency, restart, reopen/resume;
3. **CollectionPoolTests** — old-job migration states, shared claim/capability/lease/idempotent delivery/V1 compatibility;
4. **IncomingMonitoringTests** — threshold monitoring/reactivation/classifications;
5. **NegotiationAttachmentTests** — price statements, checks, attachment authorization;
6. **InspectionAcquisitionTests** — local draft/reconnect/media retry + Acquired;
7. **OrganizationIdentityTests** — direct account + temporary password + archive/scope;
8. **AuditReadModelTests** — semantic formatting/filter/paging/tenant isolation;
9. **OverviewTests** — aggregate consistency and scope;
10. browser/UI scenarios для critical flows без snapshot-only confidence.

ParserSpike test projects не расширять в рамках этого master plan.

---

# 9. Production readiness / Definition of Done Stage 1

Stage 1 завершён только когда:

- Catalog и Procurement архитектурно независимы;
- PropertyCase не требует Listing и адресуется CaseId;
- one Case supports `0..N` sources; one confirmed Catalog item cannot belong to two Cases;
- automatic + manual/Telegram-like ingress работают без fake Collector entities;
- monitoring/reactivation и distinct source classifications работают;
- existing real object resumes/reopens existing Case вместо duplicate Case;
- Procurement scope не зависит от Listing Department/Team;
- Collection работает как shared server pool; old jobs migrated safely; V1 Collector compatible;
- local Parser Agent не изменён;
- negotiations/checks/attachments/inspection/offline-draft/Acquired работают сквозно;
- Organization управляет employee lifecycle без hard delete;
- permissions/scope применяются server-side в каждой фазе;
- Audit human-readable и paged;
- Overview использует реальные scope-safe aggregates;
- raw technical vocabulary отсутствует на staff surfaces;
- migrations safe/tested from legacy Stage 1;
- legacy ListingId/Search-routing cleanup выполнен только после Phase 9 gates;
- automated + executable verification green;
- Owned Assets/LandAsset/Investor остаются out of Stage 1.

---

# 10. Dependency / exit-gate consistency check после independent review

Проверка выполнена только на четыре требуемых аспекта — без нового architecture audit.

### Dependency order

- Phase 1 сначала стабилизирует Catalog/Procurement identity, source model, manual ingress и CaseId contracts;
- Phase 2 затем исправляет server Collection ownership/claim и только после 2A строит schedules/UI в 2B;
- Phase 3 использует готовые boundary commands и stable ingestion для полного Incoming UX/monitoring;
- Phase 4 строит dossier/negotiations/checks и attachment foundation только после stable Case identity;
- Phase 5 использует Case + attachments для Inspection/Acquired;
- Phases 6–8 не меняют фундаментальную domain identity, а доводят administration/audit/overview;
- Phase 9 единственный выполняет destructive cleanup.

Противоречий dependency order после поправок нет.

### Exit gates

Каждая фаза имеет самостоятельный deployable exit gate; security/scope tests входят в ту же фазу, которая вводит read/write surface. Phase 2B не начинается до green 2A; Phase 9 cleanup не начинается без доказанного cutover.

### Migration safety

Все breaking changes идут через expand -> backfill -> compatibility -> cutover -> verify -> contract. Active Collection leases не steal, completed executor history не теряется, legacy Procurement history не пересоздаётся, source provenance не hard-delete.

### Межфазные противоречия

- минимальный manual/TakeToWork slice находится в Phase 1; Phase 3 только расширяет Incoming UX и monitoring;
- scope correctness не откладывается в Phase 6;
- attachment foundation начинается в Phase 4 и переиспользуется Phase 5;
- temporary ListingId compatibility живёт до Phase 9;
- Parser Agent остаётся вне изменений всех девяти фаз.

**Оставшихся open architecture questions, блокирующих Phase 1, нет.** Новый redesign перед implementation не требуется.

---

# 11. Что должен сделать следующий implementation-agent первым

Начать **только с Phase 1**:

1. перечитать `P0_CATALOG_PROCUREMENT_BOUNDARY.md`, Phase 1 этого master-plan и relevant current tests;
2. зафиксировать target invariants failing/characterization tests;
3. выполнить additive migration source links + case-owned facts + server source/optional identity support;
4. backfill legacy data с migration provenance;
5. реализовать minimal manual/Telegram-like ingestion;
6. реализовать DB-enforced/idempotent/concurrent-safe `TakeToWork`;
7. перевести Procurement contracts/workspace/queue/canonical route на CaseId;
8. оставить tested ListingId compatibility redirect;
9. доказать current Avito + manual/Telegram-like + scope + migration scenarios;
10. не начинать Phase 2 до executable evidence Phase 1 exit gate.

Повторный общий архитектурный аудит Stage 1 перед Phase 1 **не нужен**. Реализацию локального Parser Agent не начинать.