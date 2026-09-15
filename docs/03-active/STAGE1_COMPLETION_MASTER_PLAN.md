# Stage 1 — Completion Master Plan

**Статус:** canonical execution plan  
**Дата ревизии:** 2026-09-15  
**Рабочая ветка:** `codex/stage-1-procurement-core`  
**Аудитируемый baseline:** `b6d64e6258bf23d7263695080ff588afe3c04eae`  
**Назначение:** единая точка входа для завершения Stage 1 без повторного архитектурного исследования.

---

## 1. Как пользоваться этим документом

Этот документ заменяет роль разрозненных task notes как **главный execution-документ завершения Stage 1**. Следующий агент должен начинать с **Phase 1** и не возвращаться к повторному проектированию уже принятых границ.

При конфликте источников использовать такой приоритет:

1. `docs/03-active/P0_CATALOG_PROCUREMENT_BOUNDARY.md` и утверждённые screen specs `docs/14-ui-kit/screens/01..10`;
2. этот master plan;
3. актуальные ADR / foundation / FP-документы, если они не противоречат п.1–2;
4. старые `ACTIVE_TASK.md`, `README.md`, `START_HERE.md`, старые FP-допущения и текущая production-реализация как описание **CURRENT**, а не как целевая архитектура.

HTML prototypes в `docs/14-ui-kit/prototypes/` задают композицию и UX-reference. Их demo data, JS и технические названия не являются production contract. Реализация остаётся на существующем Blazor/component/token stack.

### 1.1. Жёсткие границы этой программы работ

- **не трогать локальный Parser/Collector Agent** (`LandErp.ParserSpike*`) до отдельной фазы после завершения серверного Stage 1;
- сервер можно менять так, чтобы старый V1 Collector продолжал работать через compatibility path;
- не реализовывать `Owned Assets`, `LandAsset`, Investor cabinet и полноценный post-purchase contour;
- Stage 1 заканчивается подтверждённым `Acquired` внутри `PropertyCase`: фактическая цена + дата + комментарий; дальнейший `LandAsset` — только будущая точка передачи;
- не создавать отдельный большой модуль/экран «Сделка»;
- старые migrations не переписывать: только новые additive/transform migrations;
- не делать глобальный UI rewrite отдельно от нужного business flow.

---

## 2. Результат ревизии: что реально есть сейчас

### 2.1. Catalog / incoming

**CURRENT**

- серверная сущность `Catalog.Domain.Listing` фактически является marketplace listing;
- источник типизирован через `LandErp.Collector.Contracts.V1.ListingSource`, то есть Catalog зависит от бинарного Collector contract;
- `ExternalId` и `Url` обязательны;
- `DepartmentId`/`TeamId` приходят из `SearchConfiguration` и сохраняются в Listing;
- ручной/Telegram/referral ingestion без fake Agent/Job отсутствует;
- отдельного production-экрана «Входящие объявления» нет: текущий `/procurement` начинает выборку от Listings.

**TARGET**

- нейтральный `CatalogItem` concept для Avito/Cian/Telegram/manual/referral/agent/other;
- server-owned source code, не зависящий от Collector enum;
- `ExternalId?`, `Url?` optional;
- автоматические observations остаются историей Collector, ручные записи имеют нормальный provenance без fake job;
- отдельный incoming read model/UI;
- действия входящего слоя: отклонить, наблюдать, связать с существующим case, `Взять в работу`.

**MIGRATION**

Физическое переименование `Listing` не обязательно в первой миграции. Можно сохранить таблицу/класс как transitional storage, но снять доменную зависимость Catalog от Collector enum, разрешить non-marketplace entries и ввести отдельный source-link к Procurement.

### 2.2. Procurement / PropertyCase

**CURRENT**

- `PropertyCase.ListingId` обязателен;
- в EF стоит unique index + required FK `PropertyCase -> Listing`;
- `ProcurementWorkspace` начинает queue от `Listings` и создаёт case через `EnsureCaseAsync(listingId)`;
- публичные contracts, commands, Razor route и card работают по `ListingId`;
- один case технически равен одному listing;
- рабочие факты case в основном читаются из актуального Listing;
- существующий manager/head workflow, assignment, task, approvals, notifications, business timeline, optimistic concurrency и append-only history — полезная база и должна быть сохранена.

**TARGET**

- `PropertyCase` — aggregate/root Procurement;
- `0..N` связанных Catalog items через отдельную подтверждённую связь (`PropertyCaseCatalogItem` / `PropertyCaseSourceLink`);
- после `Взять в работу` все Procurement read/write operations адресуются `CaseId`;
- queue начинается с `PropertyCases`;
- case содержит собственные подтверждённые/рабочие факты, а source data остаются provenance;
- удаление/закрытие/изменение внешнего объявления не разрушает case;
- Telegram + Avito + Cian могут относиться к одному case.

**MIGRATION**

Добавить source-link, backfill существующего `ListingId`, перенести минимальные case-owned facts, перевести contracts/workspace/routes на `CaseId`, затем снять обязательный FK/unique coupling. Existing business number, assignment, task, approvals, timeline и audit не пересоздавать.

### 2.3. Collection server

**CURRENT**

- `SearchConfiguration` содержит обязательные `AgentId`, `DepartmentId`, `TeamId`;
- `ServerCollectionJob` заранее содержит `AgentId`;
- `ClaimAsync` выбирает только `jobs.agent_id = current agent`;
- ingestion маршрутизирует Listing в department/team выбранного search;
- production UI заставляет администратора выбрать конкретный Agent/Department/Team;
- расписания, группы поисков и полноценная история запусков ещё не доведены до approved UX.

**TARGET**

`Search -> Schedule -> Pending Job -> shared pool -> compatible free Agent claims`.

- Search не содержит обязательный Agent/Department/Team;
- группа поисков — только организационная группировка;
- job получает actual executor при claim/lease, а не при enqueue;
- capability/source compatibility проверяется на сервере;
- все результаты организации идут в общий incoming Catalog;
- server schedule создаёт jobs; manual run остаётся доступен;
- UI показывает поиски, группы, расписания, общий пул, историю и состояние парсеров.

**Compatibility**

Текущий V1 Collector уже вызывает серверный `claim` и получает `CollectionWork`; он не обязан знать, к какому Agent search был привязан в БД. Поэтому shared-pool routing можно реализовать серверно, сохранив V1 wire shape и endpoints. Это обязательный способ не заставлять немедленно переделывать работающий локальный Parser Agent.

### 2.4. Procurement business flow

**CURRENT**

Работают базовые действия manager/head: take work, clarify, monitor, forward, return, approve/reject, notes/contact, assignment/task, notification, timeline и change signal от listing revision. Но это короткий Listing-centric workflow, а не полный Stage 1 dossier.

**TARGET**

Один PropertyCase проходит:

`входящий item -> взять в работу -> первичный анализ -> manager/head handoff -> переговоры -> quick/deep checks -> site inspection -> решение -> подтверждение покупки -> Acquired`.

Карточка case должна иметь утверждённые рабочие поверхности: `Основное / Переговоры / Проверки / Осмотр / Документы / История / Источники`.

### 2.5. Negotiations / checks / inspection / purchase

**CURRENT**

- полноценной доменной модели Negotiation/PriceStatement нет;
- quick/deep checks представлены workflow-текстом, а не структурированными результатами;
- Site Inspection отсутствует как production module/read-write model;
- purchase completion отсутствует.

**TARGET**

- переговоры различают ask / seller offer / buyer offer / agreed price и сохраняют события/условия/next step;
- quick checks и deep/legal checks хранят структурированные результаты, статус, автора, время, комментарий и blockers;
- site inspection — mobile-first checklist с server-configurable template, прогрессом, notes/media и итогом;
- `Отметить как куплено` фиксирует фактическую цену, дату, комментарий, ставит `Acquired`, пишет timeline/audit и закрывает procurement work;
- **не создавать InvestmentProject в Stage 1**. Старое FP-041 в этой части superseded approved product boundary.

### 2.6. Organization / Identity

**CURRENT**

- domain уже содержит Organization, OrgUnit, Team, Position, Employee, EmployeeAssignment и AccessScope;
- Team принадлежит Department — подходящая основа;
- UI разделён на `/organization` и `/employees`;
- управление в основном create-oriented;
- primary onboarding — invitation/email activation;
- employee/assignment UI местами показывает технические scope/role identifiers.

**TARGET**

Один раздел `Организация` с tabs:

`Структура / Сотрудники / Должности`.

- Department -> Team -> Employees;
- rename/archive/restore вместо физического удаления;
- сотрудники физически не удаляются;
- position не определяет permissions автоматически;
- администратор может создать login/password account; временный пароль показывается один раз;
- invitation-flow остаётся secondary;
- права и scope применяются сервером, UI показывает human-readable labels.

### 2.7. Audit

**CURRENT**

`/audit` напрямую выводит raw `Action`, `EntityType`, `EntityId`, user id и `Changes` JSON. Нет semantic formatting, нормального paging/filtering и безопасного технического disclosure.

**TARGET**

Основной журнал:

`кто -> что сделал -> с чем -> когда -> что изменилось`.

Нужен отдельный server read model: semantic event type/title, actor display name, target display identity, human-readable change set, server-side filters + paging. Raw action code, GUID, correlation id и JSON — только в раскрываемом `Технические детали`.

### 2.8. Overview

**CURRENT**

Home — welcome page с несколькими link cards.

**TARGET**

Операционный dashboard с server-side aggregates:

- Новые входящие;
- В закупке;
- Требуют внимания;
- Ждут решения;
- `Требует внимания`;
- `Закупка сейчас`;
- `Моя работа`;
- `Сбор данных`;
- быстрые действия;
- команда закупки только в допустимом scope.

Не добавлять BI/финансы/графики для заполнения пустого места.

### 2.9. Navigation / permissions / technical vocabulary

**CURRENT**

Sidebar содержит отдельные `Сотрудники`, `Оргструктура`, техническое `Collector`; incoming Catalog отсутствует. Domain permissions уже существуют, но screens/read models не везде scope-aware на целевом уровне.

**TARGET**

Навигация следует пользовательским областям: `Обзор`, `Входящие`, `Закупка`, `Сбор данных`, `Организация`, `Аудит` по permissions. В staff UI запрещены raw class/enum identifiers.

Обязательные display mappings, среди прочих:

- `PropertyCase` -> `объект закупки` / business name;
- `CollectionJob` -> `задание на сбор данных`;
- `CollectorAgent` -> `сборщик` / `парсер`;
- `AwaitingManualAction` -> `требует ручного действия`;
- `AccessScope.Organization` -> `Вся организация`;
- `DD` -> `глубокая проверка` / `юридическая проверка`;
- GUID/raw enum/class/correlation id -> только technical details.

---

## 3. Superseded assumptions register

Следующие старые предположения **не должны возвращаться** в реализацию:

| Старое предположение | Канонический target |
|---|---|
| Search закреплён за конкретным Agent | shared pool + capability-based claim |
| Search маршрутизирует данные через Department/Team | результаты организации идут в общий Catalog |
| `/procurement` одновременно входящие и закупка | отдельные Incoming Catalog и PropertyCase queue |
| PropertyCase существует только из Listing | PropertyCase независим, `0..N` source links |
| Procurement commands адресуются ListingId | после take-work только CaseId |
| Catalog source enum принадлежит Collector contract | server-owned Catalog source code |
| ручные данные имитируют Collector result | отдельный manual ingestion/provenance path |
| Employees и Org structure — разные top-level разделы | один `Организация` с tabs |
| invitation — единственный/главный employee flow | direct login/password creation primary, invitation secondary |
| Audit показывает raw storage fields | semantic audit read model + hidden technical details |
| Home — набор ссылок | operational Overview |
| покупка автоматически создаёт InvestmentProject | Stage 1 заканчивается Acquired; LandAsset/Projects позже |

`README.md` / `START_HERE.md` и старые FP могут содержать эти предположения. Их править после стабилизации соответствующей implementation phase, а не использовать как target.

---

## 4. Target server contracts, которые должны стабилизироваться

### 4.1. Catalog

Минимально нужны application contracts/use cases:

- `ListIncomingCatalog(...)` — server filtering/paging;
- `GetCatalogItem(id)`;
- `CreateManualCatalogItem(...)`;
- `Dismiss/Monitor CatalogItem`;
- `TakeCatalogItemToWork(catalogItemId, createNewCase | existingCaseId)`;
- `LinkCatalogItemToCase(catalogItemId, caseId, relationType)`;
- source/provenance read model.

Source classification принадлежит Server/Application, Collector source enum маппится на неё на ingestion boundary.

### 4.2. Procurement

После создания case:

- `ListPropertyCases(...)`;
- `GetPropertyCase(caseId)`;
- workflow decisions / transfer / notes / negotiation / checks / inspection / purchase — по `CaseId`;
- source changes создают signal/attention, но не перезаписывают case-owned verified facts автоматически.

### 4.3. Collection

- Search definition + revision/config;
- Search group;
- server schedule;
- enqueue/manual run;
- pending shared jobs;
- claim compatible work;
- actual AgentId появляется на lease/execution;
- job/history read model;
- V1 collector endpoints сохраняются совместимыми до отдельного Parser Agent phase.

### 4.4. Organization / Identity

- list/read Organization workspace;
- create/rename/archive/restore Department/Team/Position;
- create employee account with login + generated temporary password;
- password returned **ровно один раз** в command result, не сохраняется как retrievable plaintext;
- disable/archive employee without physical delete;
- assignment/scope/permissions managed separately from position;
- invitation remains explicit secondary action.

---

# 5. Implementation phases

Всего: **9 phases**. Phase 1 и Phase 2 — P0 и должны быть завершены до косметического расширения остальных страниц.

---

## Phase 1 — P0: разорвать обязательную Catalog -> PropertyCase связь

**Цель**  
Сделать PropertyCase самостоятельным Procurement aggregate и сохранить все существующие Stage 1 данные.

**Почему сейчас**  
Пока case обязан иметь ListingId, невозможно корректно реализовать manual/Telegram flow, несколько источников, case-owned facts, отдельную procurement queue и устойчивость к исчезновению внешнего объявления.

**Затрагиваемые модули/сущности**  
Catalog `Listing`/future `CatalogItem`, `PropertyCase`, source links, EF mappings, Procurement contracts/workspace, routes, scope evaluation, existing tests.

**Конкретные изменения**

1. Сначала добавить failing/characterization tests целевых инвариантов из `P0_CATALOG_PROCUREMENT_BOUNDARY.md`.
2. Ввести neutral relation `PropertyCaseCatalogItem` / `PropertyCaseSourceLink` с `CaseId`, `CatalogItemId`, relation kind/status, provenance/audit fields и подтверждённой uniqueness policy.
3. Добавить в PropertyCase минимальные case-owned рабочие факты, которые сейчас читаются только из Listing: display name/location/cadastral or other confirmed identifiers, working area/price fields — только реально нужные Procurement данные, без копирования всего source payload.
4. Убрать business dependency от `PropertyCase.ListingId`; transitional nullable legacy column допустим на одну миграцию.
5. Перевести `ProcurementWorkspace` на case-root reads и `CaseId` после boundary command.
6. Queue query начинает с `PropertyCases`; linked Catalog data подмешивается как source summary/signal.
7. Перевести card/decision/note/forward/return/approval contracts с ListingId на CaseId.
8. Route карточки сделать case-based, например `/procurement/{caseId:guid}`; Listing/Catalog id остаётся только у incoming actions.
9. Access checks case не вычислять через Listing.DepartmentId/TeamId; использовать organization + assignment/responsibility + AccessScope.

**Миграции**

- новая additive migration создаёт source-link table и case-owned columns;
- для каждого существующего `PropertyCase.ListingId` создать confirmed source link;
- backfill case-owned snapshot/facts, не теряя Listing/Observations;
- сохранить BusinessNumber, assignment/task, transitions, approvals, timeline, audit;
- после перевода reads/writes отдельной migration снять required FK/unique Listing coupling; старую колонку удалить только когда compatibility consumer отсутствует.

**Compatibility concerns**

- существующие Avito/Cian Listing IDs и observations остаются неизменными;
- на переходе можно иметь adapter, который по legacy ListingId находит единственный linked CaseId только для старого server UI/test path; новый contract не должен распространять legacy дальше.

**UI**  
Только минимальная адаптация route/card/queue, без полного редизайна screen 01–03 в этой фазе.

**API/application services**  
Case-centric public contracts; explicit `TakeToWork` boundary command готовится/вводится здесь или в Phase 3, но внутренний case уже не зависит от Listing.

**Tests**

- source-less PropertyCase может существовать/read;
- один case имеет 3 sources;
- source listing исчез/changed -> case жив;
- queue root = PropertyCase;
- all case commands = CaseId;
- existing manager/head concurrency, scope, restart, append-only tests сохранены в case-centric форме;
- migration backfill test на legacy dataset.

**Executable verification**

```powershell
dotnet restore LandErp.slnx --locked-mode
dotnet build LandErp.slnx --no-restore
dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj --no-build
```

Для PostgreSQL tests использовать существующий sandbox/локальную БД проекта. Проверить `Database.HasPendingModelChanges() == false`.

**Acceptance criteria**

- обязательного `PropertyCase.ListingId` в business model больше нет;
- существующий Avito case мигрирован без потери истории;
- Procurement read/write path работает по CaseId;
- case может жить при 0..N sources.

**Не входит**  
Полный incoming UI, negotiations, inspection, Collection redesign, Organization redesign.

**Перед Phase 2 должно быть истинно**  
Все P0 Catalog/Procurement domain tests green; миграция воспроизводима на clean DB и legacy Stage 1 DB.

---

## Phase 2 — P0: Collection shared pool и server-owned routing

**Цель**  
Вернуть Collection к модели общего пула и убрать скрытую маршрутизацию Catalog через конкретный Agent/Department/Team.

**Почему сейчас**  
Текущая модель генерирует новые Catalog данные с неправильной ownership semantics. Её нужно исправить до расширения incoming UI и server schedules.

**Затрагиваемые модули/сущности**  
`SearchConfiguration`, search groups/schedules, `ServerCollectionJob`, CollectorGateway, CollectionAdministration, Collection mappings/read models/UI, Catalog ingestion boundary.

**Конкретные изменения**

1. Search больше не требует `AgentId`, `DepartmentId`, `TeamId`.
2. Ввести SearchGroup как организационную группировку и server schedule model.
3. Job создаётся `Pending` без executor; хранит required source/capability/priority/search revision/config snapshot.
4. `ClaimAsync` атомарно выбирает любой совместимый pending job организации с `FOR UPDATE SKIP LOCKED`; actual AgentId/lease фиксируется при claim.
5. Expired lease возвращается в pool по действующей retry policy.
6. Ingestion создаёт/обновляет organization-wide Catalog item; department/team из search не копируются в incoming ownership.
7. Состояния jobs переводятся в human-readable read model; raw enum не идёт напрямую в UI.
8. Добавить server-side groups/schedules/history; manual run остаётся.

**Миграции**

- nullable/remove preassigned `agent_id` from jobs после переноса executor semantics в lease/job execution fields;
- remove search routing fields после backfill/compat period;
- существующие searches перевести в default group; текущие schedules/manual configs сохранить насколько возможно;
- existing pending assigned jobs либо мигрировать в shared pending pool, либо завершить drain до cutover — выбрать безопасный вариант по данным окружения.

**Compatibility concerns**

- V1 Collector wire endpoints/`CollectionWork` сохранить;
- локальный Parser Agent не читать и не менять;
- server adapter маппит старый Collector `ListingSource` -> server-owned Catalog source code;
- новый server Search creation API не требует изменения старого Agent claim loop.

**UI**  
Довести `/collectors`/replacement screen до approved `Сбор данных`: группы, поиски, расписания, shared queue, история, parsers. Термин `Collector` не использовать в основной staff navigation.

**API/application services**  
Search CRUD/archive/pause, group management, schedule, RunNow, history/paging, shared Claim.

**Tests**

- Search создаётся без Agent/Department/Team;
- два совместимых Agent конкурируют и получают разные jobs;
- несовместимый Agent job не получает;
- expired lease claimable другим Agent;
- results from any Agent land in same organization Catalog;
- current V1 registration/heartbeat/claim/accept remains green;
- schedule creates one idempotent job per due run.

**Executable verification**  
Foundation PostgreSQL tests + минимум один integration scenario с двумя registered Agent identities и одним shared queue.

**Acceptance criteria**

- Search не закреплён за машиной;
- job executor определяется при claim;
- UI больше не требует выбрать Agent/Department/Team для поиска;
- текущий локальный Parser Agent не потребовал code change.

**Не входит**  
Audit Parser desktop UX, CAPTCHA/session/browser behavior, parser refactor.

**Перед Phase 3 должно быть истинно**  
Новый incoming поток не получает routing ownership от search/agent; shared pool стабилен и покрыт concurrency tests.

---

## Phase 3 — P1: Incoming Catalog + отдельная Procurement queue

**Цель**  
Развести пользовательские поверхности `Входящие` и `Закупка` и реализовать единый вход для автоматических и ручных предложений.

**Почему сейчас**  
После Phase 1–2 доменные границы и ingestion корректны; теперь approved screens можно строить без закрепления неправильной модели.

**Затрагиваемые модули**  
Catalog application/read models, Procurement boundary service, incoming Razor page, procurement queue, navigation.

**Конкретные изменения**

- server-owned Catalog source codes: Avito, Cian, Telegram, Manual/Referral/Agent/Other с extensible representation;
- разрешить `ExternalId?`/`Url?` для non-collector items;
- command `CreateManualCatalogItem` с provenance actor/time/comment;
- incoming statuses/actions: new/reviewed/monitoring/dismissed/linked/in-work согласно screen spec, без лишней универсальной state machine;
- `TakeToWork` создаёт новый PropertyCase или связывает item с существующим; операция идемпотентна;
- manual item не создаёт CollectionJob/Observation;
- incoming list: server paging/filter/search/source/status/age/price/area + detail drawer;
- procurement queue показывает только PropertyCases и case-specific next action/responsibility/sources summary;
- navigation получает отдельные `Входящие` и `Закупка`.

**Миграции**  
Additive Catalog columns/status/source/provenance; безопасная конверсия current ListingSource -> server source code; external id/url constraints ослабить для manual paths, сохранив uniqueness policy для marketplace identity.

**Compatibility concerns**  
Collector ingestion продолжает писать observations для automatic sources; manual rows обходят observation table.

**UI**  
Следовать `01-incoming-listings.md` и `02-procurement-queue.md`; prototype используется только как composition reference.

**API/application services**  
Paged incoming query, item detail, manual create, dismiss/monitor, link/take-work; separate paged case queue.

**Tests**

- Telegram item без URL/external id виден во входящих;
- dismiss/monitor manual = same semantics as Avito;
- take-work создаёт ровно один case;
- link to existing case не создаёт duplicate;
- Avito + Telegram + Cian -> one case;
- source price update не перезаписывает verified case facts.

**Executable verification**  
Integration tests + интерактивный smoke: создать manual Telegram item -> увидеть `/incoming` -> взять в работу -> увидеть `/procurement` -> открыть case.

**Acceptance criteria**  
Incoming и Procurement физически/логически разделены; оба работают с реальными server data и permissions.

**Не входит**  
Полный dossier tabs, inspection, purchase completion.

**Перед Phase 4 должно быть истинно**  
Все четыре входа (Avito, manual, Telegram-like, multi-source) приводят к одному и тому же case-centric procurement flow.

---

## Phase 4 — P1: PropertyCase dossier, workflow, переговоры и проверки

**Цель**  
Довести карточку объекта закупки до рабочего инструмента менеджера и руководителя.

**Почему сейчас**  
Case identity, sources и queue уже стабилизированы; можно добавлять бизнес-функции без повторной миграции корня aggregate.

**Затрагиваемые модули**  
Procurement, Workflow, Negotiations, checks/DD read-write models, documents references, timeline, notifications.

**Конкретные изменения**

- реализовать approved tabs: `Основное / Переговоры / Проверки / Осмотр / Документы / История / Источники`;
- сохранить существующие manager/head transitions, но привести stage/next-action vocabulary к бизнес-процессу;
- явно поддержать manager -> head -> return -> rework -> decision;
- в `Основное` хранить/edit case-owned verified facts и отдельно показывать discrepancies sources;
- Negotiation: events + PriceStatement Ask/SellerOffer/BuyerOffer/Agreed, conditions, contact/channel, next step;
- Quick checks как структурированные простые проверки первичного отбора;
- Deep/legal checks как структурированные check records/blockers/results, без вывода аббревиатуры DD пользователю;
- documents references связывать с case/check/inspection where applicable;
- history/timeline объединяет business events, но не подменяет system audit;
- source changes создают attention item, а решение применить новое значение к case — явное действие пользователя.

**Миграции**  
Новые tables/columns для Negotiation/PriceStatement/check results; indexes по CaseId/status/next action; существующую timeline не переписывать.

**Compatibility concerns**  
Старые stage IDs можно временно сохранить как storage codes при наличии display mapping; migration state machine делать только если новые переходы реально требуют новых canonical codes.

**UI**  
`03-property-case.md` — canonical. Raw class/enum names запрещены.

**API/application services**  
Case summary/details, verified facts update with concurrency, negotiation commands, check commands, source discrepancy actions, workflow transitions.

**Tests**

- negotiation price types never overwrite each other;
- agreed != acquired;
- return/rework preserves history/tasks;
- blockers visible and permission-protected;
- source discrepancy does not mutate verified field implicitly;
- scope rules for manager/head/organization remain enforced.

**Executable verification**  
E2E integration: manager takes item -> fills primary facts/checks -> contacts seller -> forwards -> head returns -> manager fixes -> head approves deeper work.

**Acceptance criteria**  
PropertyCase can be managed through approved dossier without falling back to Listing-centric fields or raw DB concepts.

**Не входит**  
Field inspection implementation and final Acquired transition — Phase 5.

**Перед Phase 5 должно быть истинно**  
Case has enough structured business state to make and explain a purchase decision.

---

## Phase 5 — P1: Site Inspection + purchase completion to Acquired

**Цель**  
Закрыть последний business gap полного Stage 1 procurement cycle.

**Почему сейчас**  
Осмотр и покупка должны опираться на уже готовые case/check/negotiation данные.

**Затрагиваемые модули**  
Procurement/Inspection, files/doc references, Workflow, timeline/audit.

**Конкретные изменения**

- server-configurable inspection template/checklist; не hard-code список только в Razor;
- inspection instance per case with item result/status/note/media, progress, overall conclusion;
- mobile-first screen по `04-site-inspection.md`;
- offline-first не требуется, но UX должен терпеть медленную мобильную сеть и частичные сохранения;
- action `Отметить как куплено`: actual price, acquisition date, comment обязательной/разумной валидации;
- transition to `Acquired`, completion of active procurement task/assignment state по согласованному правилу;
- timeline + audit event;
- future handoff contract: после Stage 1 этот факт сможет создать/активировать LandAsset отдельной последующей задачей, но сейчас LandAsset не создаётся.

**Миграции**  
Inspection template/instance/results + acquisition facts on case or dedicated Procurement-owned purchase record; никаких InvestmentProject/LandAsset tables в этой phase.

**Compatibility concerns**  
FP-041 `InvestmentProject` creation считается superseded для Stage 1. Сохранить только полезную семантику `Approved` vs confirmed `Acquired`.

**UI**  
`04-site-inspection.md` + purchase modal/reference из approved PropertyCase set. Нет отдельного top-level `Сделка`.

**API/application services**  
Get/start/save/complete inspection; mark acquired; read purchase summary.

**Tests**

- checklist progress/reload/concurrency;
- completed result preserves notes/media references;
- cannot acquire twice;
- actual price/date/comment persisted;
- Acquired appears in timeline/audit and leaves active procurement queue;
- PropertyCase remains readable after acquired.

**Executable verification**  
Mobile viewport smoke + PostgreSQL restart test + complete E2E `incoming -> case -> inspection -> acquired`.

**Acceptance criteria**  
Полный Stage 1 procurement business flow проходит от входящего предложения до `Acquired` без future modules.

**Не входит**  
Owned Assets, LandAsset detail, works, lots, investors, project finance.

**Перед Phase 6 должно быть истинно**  
Business core Stage 1 закончен; дальнейшие phases доводят administration/read models/production UX.

---

## Phase 6 — P2: Organization, employee creation, permissions и scope UX

**Цель**  
Сделать организационную модель реально администрируемой без технических обходов.

**Почему сейчас**  
Business flow уже работает; теперь его нужно безопасно отдавать реальным сотрудникам с понятными account/scope controls.

**Затрагиваемые модули**  
Organization, IdentityAccess, AppShell/navigation, employee onboarding UI.

**Конкретные изменения**

- объединить `/organization` и `/employees` в один `Организация` с tabs;
- Structure = Department -> Team -> Employees; Team не показывать как «подотдел»;
- rename/archive/restore для Department/Team/Position; employee disable/archive/restore; hard delete не предоставлять;
- direct account creation: login + generated temporary password; one-time display/copy screen;
- при первом входе потребовать смену temporary password по безопасной Identity policy;
- invitation flow оставить secondary explicit action;
- position и permission role не связывать автоматически;
- scope editor human-readable: `Свои`, `Назначенные объекты`, `Команда`, `Отдел`, `Вся организация`;
- navigation visibility и server authorization должны совпадать по capability, но скрытие ссылки не заменяет server authorization;
- проверить case/catalog/overview scope queries на Department/Team/Own/Assigned/Organization.

**Миграции**  
Только если нужен explicit temporary-password/change-required marker или archival metadata; plaintext password не хранить.

**Compatibility concerns**  
Existing invited users/assignments сохраняются. Invitation activation endpoints не ломать.

**UI**  
`08-organization.md` canonical.

**API/application services**  
DirectCreateEmployeeAccount, reset temporary credential if approved, rename/archive/restore admin commands, scoped read models.

**Tests**

- create employee -> password returned once -> login -> forced change -> work in scope;
- no API returns temporary password later;
- archived employee cannot work but history remains;
- position change does not silently change permission role;
- all scope modes constrain Catalog/Procurement/Overview correctly.

**Executable verification**  
Identity integration + browser login smoke under owner/head/manager accounts.

**Acceptance criteria**  
Administrator can create and place a new employee into real workflow without email invitation or raw GUID/role handling.

**Не входит**  
Enterprise SSO, external HR integration.

**Перед Phase 7 должно быть истинно**  
All primary Stage 1 screens are permission/scope correct for multiple users.

---

## Phase 7 — P2: semantic Audit + системная очистка технических имён

**Цель**  
Сделать аудит пригодным для руководителя/администратора и убрать технические утечки из staff UI.

**Почему сейчас**  
После стабилизации business/admin actions известен реальный набор событий и можно строить устойчивое semantic formatting.

**Затрагиваемые модули**  
Audit read services, Organization actor lookup, entity display identities, Razor pages/components, labels.

**Конкретные изменения**

- server-side paged/filterable audit query: date, actor, event category, object, search;
- semantic formatter per action family (Catalog, Procurement, Collection, Organization, Identity);
- human-readable before -> after fields;
- business object title/business number instead of GUID in primary row;
- technical drawer contains action code, entity type/id, correlation id, raw JSON;
- grep/audit всех production Razor/read models на raw enum/class/GUID leakage;
- centralized display maps where values cross multiple screens; не прятать бизнес-semantics в случайных Razor ternaries.

**Миграции**  
Обычно не нужны; при необходимости добавить normalized metadata только новой migration, не переписывать historical audit rows.

**Compatibility concerns**  
Old audit events без semantic metadata должны форматироваться best-effort и всегда иметь technical fallback.

**UI**  
`09-audit.md` canonical.

**API/application services**  
AuditQuery + SemanticAuditRow/Change DTO + technical details endpoint/field gated by permission.

**Tests**

- representative events format deterministically;
- unknown legacy action falls back safely;
- server paging/filtering isolation by organization;
- no raw GUID/action JSON in primary audit row;
- technical details available only to allowed users.

**Executable verification**  
Seed actions from all Stage 1 modules -> open audit -> filter -> expand technical details -> verify no N+1 explosion on page query.

**Acceptance criteria**  
Пользователь может понять «кто что сделал» без знания class/enum/storage names.

**Не входит**  
SIEM/export pipeline unless already trivial.

**Перед Phase 8 должно быть истинно**  
Cross-cutting UI vocabulary is human-readable and audit can explain all important Stage 1 actions.

---

## Phase 8 — P2: operational Overview

**Цель**  
Сделать `/` ежедневной рабочей точкой входа, а не каталогом ссылок.

**Почему сейчас**  
Dashboard должен агрегировать уже стабилизированные Catalog, Procurement, Collection, tasks and scopes.

**Затрагиваемые модули**  
Overview/read models, Catalog, Procurement, Workflow tasks, Collection health, Organization scope.

**Конкретные изменения**

- один server Overview query/read service вместо N независимых UI-запросов;
- metrics: new incoming, in procurement, attention, waiting decision;
- attention feed с actionable reason/age/link;
- procurement now by stage/next action in компактной форме;
- `Моя работа` из assignments/tasks/notifications;
- Collection health: searches due/failing/manual action + parser availability без raw status names;
- quick actions permission-aware;
- team procurement block только когда scope позволяет;
- counts и rows используют одинаковые scope/filter semantics с исходными модулями.

**Миграции**  
Не требуются, если aggregates эффективны; materialized/read table вводить только при доказанной необходимости.

**Compatibility concerns**  
Не дублировать business rules внутри dashboard; вызывать/переиспользовать query predicates/services модулей.

**UI**  
`10-overview.md` canonical. Без декоративных графиков/финансов.

**API/application services**  
`GetOperationalOverview(subject, ...)` или эквивалентный server-side read model.

**Tests**

- counts equal module queries under same scope;
- manager sees own/team-permitted work; foreign organization invisible;
- head sees waiting decisions;
- collection attention links to correct management item;
- empty states useful.

**Executable verification**  
Seed representative data for manager/head/owner -> compare overview counters with source lists -> browser smoke.

**Acceptance criteria**  
После входа пользователь сразу видит проблемы, ожидающие решения и следующий шаг.

**Не входит**  
BI trends, ROI dashboards, project finance.

**Перед Phase 9 должно быть истинно**  
Все approved Stage 1 user surfaces 01–04, 07–10 работают на реальных server data.

---

## Phase 9 — P2/P3: Stage 1 hardening, migration cleanup и completion gate

**Цель**  
Удалить переходные хвосты, доказать сквозную работоспособность и сделать документацию соответствующей production.

**Почему сейчас**  
Compatibility paths нельзя удалять до завершения всех потребителей; документацию нельзя объявлять актуальной раньше кода.

**Затрагиваемые области**  
All Stage 1 modules, migrations, tests, docs, observability, navigation.

**Конкретные изменения**

- удалить только доказанно неиспользуемые legacy ListingId Procurement adapters/fields;
- удалить Agent/Department/Team binding из Search schema после compatibility window;
- cleanup obsolete routes/components и дублирующие read models;
- обновить `ACTIVE_TASK.md`, `README.md`, `START_HERE.md`, relevant FP status/notes так, чтобы они больше не учили старой модели;
- проверить indexes/query plans для incoming, case queue, audit paging, overview;
- проверить safe exception/error states, concurrency messages, empty/loading/error states;
- проверить accessibility/responsive behavior approved screens;
- сформировать final Stage 1 report с migration/verification evidence.

**Миграции**  
Только cleanup migration после доказанного backfill/cutover. Existing history migrations не редактировать.

**Compatibility concerns**  
Перед удалением V1 server compatibility убедиться, что текущий локальный Parser Agent всё ещё подключается. Сам Agent по-прежнему не рефакторить: отдельная следующая задача.

**UI**  
Final vocabulary/nav/responsive pass; никаких новых функций ради polish.

**API/application services**  
Remove deprecated aliases only after tests prove no production caller in server; Collector V1 retained until dedicated Parser phase.

**Tests**

- full Foundation test suite;
- migration from clean DB;
- migration from captured Stage 1 legacy shape/data;
- backup/restore if existing sandbox supports it;
- restart persistence;
- authorization negative tests;
- idempotency/concurrency tests;
- all E2E scenarios below.

**Executable verification**

```powershell
dotnet restore LandErp.slnx --locked-mode
dotnet build LandErp.slnx --no-restore
dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj --no-build
```

Дополнительно выполнить существующий project foundation script, если локальная среда настроена:

```powershell
pwsh ./scripts/Test-Foundation.ps1
```

Запустить server и выполнить browser smoke на desktop + mobile inspection viewport.

**Acceptance criteria**

- no pending model changes;
- clean build/tests;
- all E2E scenarios green;
- docs match current implementation;
- production code не содержит обязательных legacy couplings, перечисленных в superseded register;
- Stage 1 можно использовать реальными сотрудниками без ручного DB вмешательства.

**Не входит / P3 optional**  
Дополнительная визуальная полировка, редкие admin conveniences, analytics, parser desktop improvements, post-purchase modules.

**Stage 1 complete when**  
Все обязательные P0/P1/P2 criteria выполнены; P3 не блокирует релиз, если явно отложен и не влияет на безопасность/целостность/рабочий бизнес-процесс.

---

# 6. Сквозная verification matrix

Каждый сценарий обязан иметь automated integration coverage там, где это разумно, плюс минимум один executable/browser smoke для пользовательского пути.

| Scenario | Где закрывается |
|---|---|
| Avito -> Входящие -> Взять в работу -> полный PropertyCase -> Куплено | P1–P5 |
| Ручное предложение -> Входящие -> тот же полный цикл | P1, P3–P5 |
| Telegram item -> Catalog -> PropertyCase | P1/P3 |
| Telegram + Avito + Cian -> один объект закупки | P1/P3/P4 |
| Внешнее объявление удалено -> внутренний case продолжает жить | P1 |
| Менеджер -> руководитель -> возврат -> доработка -> решение | P4 |
| Осмотр с мобильного checklist | P5 |
| Создание сотрудника -> login + temp password -> вход -> работа в scope | P6 |
| Создание поиска -> расписание -> общий Job -> claim свободным compatible Agent -> результаты во входящие | P2/P3 |
| Audit event -> нормальное описание -> technical details отдельно | P7 |
| Главная -> пользователь сразу видит свои проблемы/следующие действия | P8 |

Дополнительные обязательные integrity scenarios:

- повтор `TakeToWork` не создаёт второй case;
- concurrent decisions дают понятный optimistic concurrency conflict;
- old/foreign organization cannot read case/catalog/audit;
- external source change сохраняет source history и не silently overwrites verified case fact;
- archived employee/unit/position остаются в исторических ссылках;
- duplicate collector delivery остаётся idempotent;
- two agents cannot lease same job simultaneously;
- Acquired cannot happen twice;
- audit/timeline/observations остаются append-only там, где это уже заявлено моделью.

---

# 7. Migration strategy как единая программа

Миграции выполнять **эволюционно**, не одним destructive refactor:

1. **Expand** — добавить source links, nullable/new columns, new read/write structures;
2. **Backfill** — deterministic server/data migration существующих cases/searches;
3. **Dual compatibility** — старые данные читаются, новые commands уже пишут target model;
4. **Cutover** — routes/contracts/queries переходят на CaseId/shared pool/server source code;
5. **Verify** — clean + legacy migration tests, data counts/invariants;
6. **Contract** — удалить legacy required FKs/columns/adapters только отдельной последующей migration.

Нельзя:

- редактировать старые migration files;
- пересоздавать Listing/Observation history;
- менять BusinessNumber существующих cases;
- «лечить» миграцию удалением тестовой/production-like БД;
- создавать fake Agent/Job для ручных Catalog items.

---

# 8. Test architecture после cutover

Существующий `ProcurementTests.ManagerHeadForwardReturnScopesRevisionsHistoryAuditAndRestart` полезен по охвату, но сейчас цементирует старую модель. Его нужно разложить/переписать так, чтобы сохранить сильные проверки и убрать ложные assumptions.

Минимальные server integration suites:

1. **CatalogBoundaryTests** — manual/Telegram, source ownership, take/link, multi-source, source changes;
2. **ProcurementWorkflowTests** — case-centric transitions, assignments, approvals, concurrency, restart;
3. **CollectionPoolTests** — group/schedule/shared claim/capability/lease/idempotent delivery;
4. **InspectionAcquisitionTests** — checklist + Acquired;
5. **OrganizationIdentityTests** — direct account + temporary password + archive/scope;
6. **AuditReadModelTests** — semantic formatting/filter/paging/tenant isolation;
7. **OverviewTests** — aggregate consistency and scope;
8. browser/UI scenarios для critical flows без snapshot-only confidence.

ParserSpike test projects не расширять в рамках этого master plan.

---

# 9. Production readiness / Definition of Done Stage 1

Stage 1 нельзя считать завершённым только потому, что страницы визуально похожи на prototypes.

Обязательный DoD:

- Catalog и Procurement архитектурно независимы;
- PropertyCase не требует Listing и адресуется CaseId;
- incoming поддерживает automatic + manual/Telegram-like sources;
- one case поддерживает multiple sources;
- Collection работает как shared server pool и schedules не закрепляют search за машиной;
- manager/head workflow, negotiations, checks, inspection и Acquired работают сквозно;
- Organization позволяет создать сотрудника и управлять lifecycle без hard delete;
- permissions/scope применяются server-side;
- Audit human-readable и paged;
- Overview использует реальные aggregates;
- raw technical vocabulary отсутствует на staff surfaces;
- migrations safe и tested from legacy Stage 1;
- automated + executable verification green;
- локальный Parser Agent остаётся работоспособным, но его внутренности не были переделаны;
- Owned Assets/LandAsset/Investor remain explicitly out of Stage 1.

---

# 10. Что должен сделать следующий агент первым

Начать **только с Phase 1**:

1. перечитать `P0_CATALOG_PROCUREMENT_BOUNDARY.md` и эту Phase 1;
2. зафиксировать target invariants тестами;
3. спроектировать конкретную additive EF migration `PropertyCase <-> CatalogItem source links + case-owned facts` в рамках уже принятого target;
4. выполнить migration/backfill;
5. перевести server contracts/workspace на CaseId;
6. доказать green migration + regression suite;
7. не начинать Phase 2, пока Phase 1 acceptance criteria не подтверждены executable evidence.

Повторный общий архитектурный аудит Stage 1 перед Phase 1 **не нужен**. Если код успел измениться после baseline этого документа — проверить только diff относительно `b6d64e6258bf23d7263695080ff588afe3c04eae` и скорректировать affected steps, не пересобирая roadmap заново.
