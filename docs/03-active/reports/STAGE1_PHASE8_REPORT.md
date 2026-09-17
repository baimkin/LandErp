# Stage 1 completion — Phase 8

Дата: 2026-09-17. Ветка: `codex/stage1-phase8-overview`.
Baseline: `6eb9853` (`main` после принятой Phase 7).
Статус: реализация завершена, ожидает приёмки владельцем.

## Результат

Старая главная `/` полностью заменена operational Overview по эталону
`landerp_overview_v1_4_market_per_group_production_fonts.html`. Демонстрационные
записи макета в production не переносились. Страница строится из одного bounded
server-owned `IOverviewService` и показывает только разрешённые пользователю данные:

- новые входящие, активную закупку, требующие внимания и ожидающие решения;
- attention feed с причиной, возрастом, следующим действием и прямой ссылкой;
- текущий поток закупки и bounded `Мою работу` из tasks и notifications;
- состояние Collection бизнес-формулировками без raw job statuses;
- permission-aware быстрые действия и scoped загрузку команды;
- полноширинный `Рынок по группам поиска` и paged окно всех групп.

Списки Overview ограничены на сервере: attention и `Моя работа` — 5 строк,
команда — 8, compact market — 5, окно групп — до 50 строк на страницу. Для
сортировок рынка по вычисляемым значениям используется bounded candidate set до
500 групп; обычная сортировка по названию имеет полноценную server-side пагинацию
по всем группам и поиск позволяет сузить набор.

Deep links быстрых действий подключены к существующим рабочим страницам:
ручное добавление открывает соответствующий диалог входящих, `Мои объекты`
включает server-side фильтр по текущему сотруднику, ссылки этапов применяют
поддерживаемый фильтр очереди.

## Рынок по группам поиска

Добавлена отдельная persisted-конфигурация для каждой Search Group:

- период `7 / 30 / 90 / 180` дней;
- разрешённые source-derived типы участков;
- включительные min/max границы цены за сотку;
- optimistic concurrency `Version` и audit изменения.

Новые группы получают собственные настройки по умолчанию. Forward migration
создаёт настройки и backfill для уже существующих групп. Один общий dashboard
config и отдельный config модального окна не создавались.

Median, average и counts вычисляются PostgreSQL на сервере. Выборка связывается
через Search Group → Search Configuration → Collection Job → Observation → Listing,
а затем дедуплицируется по `group_id + listing_id`. Поэтому повторные наблюдения и
workflow-сущности не умножают одно объявление. Все dispositions, включая
отклонённые/архивные рабочие состояния, остаются допустимыми. `Fake` исключается
безусловно и считается отдельно в `FakeExcludedCount`; type/price/invalid-value
отсечения входят в `ExcludedCount`. UI получает готовую read model, а не массив
объявлений.

## Scope, permissions и tenant isolation

Procurement Overview и Queue V2 используют общий `ProcurementVisibility` для
Organization/Department/Team/Assigned/Own scope. `AccessContext` текущего сотрудника
разрешается сервером; permission checks не дублируются в UI. Настройки рынка
доступны только organization-level пользователям с правами управления Collection.
Все запросы включают `OrganizationId`; foreign tenant получает пустые показатели
и не видит группы или строки другой организации.

## Миграция

Добавлена forward migration `20260917094153_Phase8OperationalOverview`:

- `collection.search_group_market_settings`;
- FK к организации и Search Group;
- уникальность одной конфигурации на группу;
- PostgreSQL comments для таблицы и бизнес-полей;
- backfill индивидуальных defaults для существующих групп.

Runtime grants обновлены в LocalSetup и PostgreSQL sandbox. Старые migrations не
изменялись. Migration к общей локальной `landerp_local` не применялась.

## Verification

| Проверка | Фактический результат |
|---|---|
| Release build `LandErp.Server.csproj` в отдельный output | green, 0 warnings / 0 errors |
| Release build `LandErp.LocalSetup.csproj` | green, 0 warnings / 0 errors |
| `OverviewTests` | green, 2/2 |
| Overview + Queue V2 + targeted Collection scheduling | green, 5/6; один старый helper defect исправлен |
| Повтор только ранее упавшего Queue V2 paging test | green, 1/1 |
| Migration chain + `HasPendingModelChanges` внутри Overview fixtures | green |
| `git diff --check` | green; только ожидаемые Windows line-ending notices |

Проверены: server-side median/average, дедупликация повторных observations,
участие `Dismissed`, отдельное исключение `Fake`, независимость настроек групп,
stale-permission denial, bounded compact projection, tenant isolation, Team scope,
`Моя работа`, team workload и фильтр `Мои объекты`.

Browser automation по прямому требованию владельца не запускалась. Полный suite
и unrelated проверки «на всякий случай» не выполнялись.

## Интеграция Procurement V2 после review

После реализации Overview перенесён коммит `e9900a2` с рабочей семантикой
Procurement V2. В текущей ветке он зафиксирован как `b2e2e70`. Конфликты с
Phase 8 разрешены с сохранением обоих независимых фильтров:

- `MineOnly` для перехода Overview «Мои объекты»;
- `PriceChangedOnly` для очереди с реальным изменением цены.

Сохранены query-параметры Overview, общий tenant-safe `ProcurementVisibility`,
новые server-side счётчики этапов, поиск по продавцу/CaseId, стартовая цена и
дельта цены. Дублировавшийся scope-switch в `ProcurementWorkspace` удалён:
Workspace, Queue V2 и Overview теперь используют один predicate для
`Own / AssignedObjects / Team / Department / Organization`.

Добавлены targeted regression assertions для сочетания `MineOnly +
PriceChangedOnly`, server-side price counts/filter, стартовой цены и дельты,
поиска по продавцу/CaseId, а также одинаковой видимости Workspace и Queue V2
для Team, Department, Own и AssignedObjects.

Фактическая проверка объединённого состояния:

| Проверка | Результат |
|---|---|
| Release build `LandErp.Server.csproj` в отдельный временный output | green, 0 warnings / 0 errors |
| Новый объединённый price semantics scenario | green, 1/1 |
| `ProcurementQueueV2ReadTests` | green, 3/3 |
| `OverviewTests` | green, 2/2 |
| Сквозной `CaseScopesUseResponsibilityAndCatalogRemainsOrganizationShared` | green, 1/1 |

Запущенный владельцем Server не останавливался; тестовые сборки направлялись во
временный output. Миграции и browser automation не запускались.

## Известные ограничения

- Тип участка выводится из source title/description по той же прикладной идее,
  что и Incoming: это source-derived классификация, не подтверждённый ВРИ.
- Value-based сортировки окна групп намеренно ограничены 500 кандидатами, чтобы
  Overview не загружал неограниченный список. По названию и поиску группы доступны
  обычной server-side пагинацией без этого ограничения.
- Визуальный smoke-test браузером не выполнялся; Razor/CSS прошли compile
  verification. Ручная визуальная приёмка остаётся за владельцем.
- Materialized projection не добавлялась: измеренной необходимости нет.

Phase 9 не начиналась. Push не выполнялся и требует принятия владельцем.
