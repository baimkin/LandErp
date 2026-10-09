# Отчёт — пользовательские доработки 5–8 октября 2026 года

Дата начала: 09.10.2026. Ветка: `main`.

Production, production-БД и рабочий сервер не изменялись. Браузерные проверки
не выполняются; визуальная приёмка остаётся владельцу.

## Этап 1. Очередь закупки — фильтры и сортировка

Реализовано:

- выбранный ответственный сохраняется в серверном запросе конкретной воронки;
- фильтр, список значений и колонка «Ответственный» используют ответственность
  PropertyCase (`Assignment.EmployeeId`), а не исполнителя ближайшей задачи;
- таблица выбранной воронки и канбан используют один и тот же смысл фильтра;
- добавлена серверная сортировка по имени ответственного в обе стороны;
- равные имена получают стабильный порядок по номеру PropertyCase и `CaseId`,
  поэтому разбиение на страницы не меняет состав;
- заголовок колонки управляет сортировкой и сообщает направление через
  `aria-sort`;
- исправлен существовавший пропуск `CancellationToken` в тесте экономики,
  который ранее блокировал компиляцию профильного тестового проекта.

Изменённые файлы:

- `docs/03-active/ACTIVE_TASK.md`;
- `src/LandErp.Application/Modules/Procurement/Public/ProcurementQueueV2ReadContracts.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.Queries.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.Projection.cs`;
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor`;
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor.css`;
- `tests/LandErp.Foundation.Tests/KanbanTests.cs`;
- `tests/LandErp.Foundation.Tests/MarketDemandTests.cs`.

Проверки:

- `dotnet --version` → `10.0.401`;
- `dotnet build src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore`
  → успешно, 0 ошибок, 0 предупреждений;
- `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --filter "FullyQualifiedName~KanbanTests.BoardManagerFilterKeepsCountsCardsAndPaginationInOneScope" --no-restore`
  → 1/1 Passed.

Миграции: не требовались.

Коммит и push: `abad324` (`origin/main`).

## Этап 2. Системное отклонение и возврат в канбане

Реализовано:

- у стадии воронки появился явный признак «Цель системного отклонения»;
- в каждой активной воронке настройка требует ровно одну активную отрицательную
  конечную стадию с этим признаком;
- новая и стандартная воронки сразу получают цель «Не подходит»;
- при бизнес-решении «Отклонить» все активные участия PropertyCase во всех его
  воронках атомарно переходят в настроенные цели без запуска туннелей;
- если хотя бы одна воронка недоступна или не имеет корректной цели, откатываются
  и бизнес-решение, и все канбан-переходы;
- возобновление отклонённого PropertyCase возвращает каждое участие в последнюю
  доступную рабочую стадию из истории, а при её удалении — в начальную рабочую;
- возобновление из режима наблюдения не меняет канбан;
- системные переходы записываются отдельно как `BusinessReject` и
  `BusinessResume`, с понятными записями в бизнес-истории;
- миграция выбирает существующую активную отрицательную стадию, а если её нет,
  добавляет «Не подходит», не меняя production автоматически.

Изменённые файлы:

- `src/LandErp.Application/Modules/Procurement/Domain/KanbanModels.cs`;
- `src/LandErp.Application/Modules/Procurement/Public/KanbanContracts.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/KanbanMappings.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/KanbanProvisioning.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/KanbanWorkspace.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/KanbanDecisionCoordinator.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs`;
- `src/LandErp.Infrastructure/Migrations/20261009083422_KanbanRejectionTarget.cs`;
- `src/LandErp.Infrastructure/Migrations/20261009083422_KanbanRejectionTarget.Designer.cs`;
- `src/LandErp.Infrastructure/Migrations/LandErpDbContextModelSnapshot.cs`;
- `src/LandErp.Server/Components/Procurement/KanbanSettings.razor`;
- `tests/LandErp.Foundation.Tests/KanbanTests.cs`.

Проверки:

- `dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore`
  → успешно, 0 ошибок, 0 предупреждений;
- `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LandErp.Foundation.Tests.KanbanTests"`
  → 10/10 Passed, включая миграционный подъём, заполнение существующей и
  неполной воронки, многовороночное отклонение, восстановление/запасной маршрут
  и атомарный откат.
- `dotnet ef migrations has-pending-model-changes ...` → модель полностью
  соответствует последней миграции.

Миграция: `20261009083422_KanbanRejectionTarget`; создана, но к production не
применялась.

Коммит и push: `5a23790` (`origin/main`).

## Этап 3. Зоны поиска и участие в медиане

Реализовано:

- `SearchGroup` представлен пользователю как зона поиска, а право создавать и
  управлять своими зонами отделено от управления Parser и поисками;
- новая зона получает владельца-сотрудника; обычный владелец может изменять и
  архивировать только свои зоны, а старые зоны без владельца остаются
  административными;
- ProcurementHead, Administrator и Owner могут управлять всеми зонами своей
  организации; проверка организации и владения выполняется сервером;
- сотрудник только с правом на зоны открывает страницу «Поиски и парсинг», но
  видит на ней лишь собственные зоны без парсеров, поисков, очереди и истории;
- страница управления поисками получила отдельный блок зон, создание,
  переименование, порядок и архивирование; терминология интерфейса приведена к
  «зонам», а во входящих сохранена ссылка на эту страницу;
- одиночное и массовое изменение участия в медиане обязательно получает зону,
  проверяет право на неё и фактическое происхождение объявления из её поиска;
  выбранные строки, весь результат и исключения не обходят эту проверку;
- владелец зоны меняет медиану только для результатов своей зоны, а
  ProcurementHead, Administrator и Owner — для результатов любой зоны
  организации;
- сохранён единый `Listing.IncludeInCalculation`: изменение сразу учитывается
  во всех зонах объявления, отдельное хранение медианы по зонам не добавлялось;
- идентификатор зоны включён в аудит пользовательского изменения медианы;
- добавлена миграция nullable-владельца зоны и отдельного права сотрудника;
  существующие зоны остаются с `NULL` и требуют повышенной роли для управления.

Изменённые файлы:

- `src/LandErp.Application/Modules/Catalog/Public/CatalogCalculationContracts.cs`;
- `src/LandErp.Application/Modules/Catalog/Public/IncomingCatalogReadContracts.cs`;
- `src/LandErp.Application/Modules/Collection/Domain/CollectionModels.cs`;
- `src/LandErp.Application/Modules/Collection/Public/CollectionServices.cs`;
- `src/LandErp.Application/Modules/IdentityAccess/Domain/PermissionModels.cs`;
- `src/LandErp.Application/Modules/IdentityAccess/Public/AccessContracts.cs`;
- `src/LandErp.Infrastructure/Modules/Catalog/CatalogCalculationService.cs`;
- `src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogReadService.cs`;
- `src/LandErp.Infrastructure/Modules/Collection/CollectionAdministration.cs`;
- `src/LandErp.Infrastructure/Modules/Collection/CollectionMappings.cs`;
- `src/LandErp.Infrastructure/Modules/IdentityAccess/EmployeeAccessService.cs`;
- `src/LandErp.Infrastructure/Modules/Organization/OrganizationWorkspace.cs`;
- `src/LandErp.Infrastructure/Persistence/LandErpDbContext.cs`;
- `src/LandErp.Infrastructure/Persistence/ModelConventions.cs`;
- `src/LandErp.Infrastructure/Migrations/20261009085314_SearchGroupOwnership.cs`;
- `src/LandErp.Infrastructure/Migrations/20261009085314_SearchGroupOwnership.Designer.cs`;
- `src/LandErp.Infrastructure/Migrations/LandErpDbContextModelSnapshot.cs`;
- `src/LandErp.Server/Components/AccessV1Ui.cs`;
- `src/LandErp.Server/Components/Pages/Collectors.razor`;
- `src/LandErp.Server/Components/Pages/Collectors.razor.cs`;
- `src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor`;
- `src/LandErp.Server/Components/Pages/OrganizationPage.razor`;
- `tests/LandErp.Foundation.Tests/CollectionSchedulingTests.cs`;
- `tests/LandErp.Foundation.Tests/EmployeeAccessSettingsTests.cs`;
- `tests/LandErp.Foundation.Tests/MarketParticipantsTests.cs`;
- `tests/LandErp.Foundation.Tests/MedianParticipationTests.cs`;
- `tests/LandErp.Foundation.Tests/ProcurementTests.cs`.

Проверки:

- `dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore`
  → успешно, 0 ошибок, 0 предупреждений;
- профильный Release-прогон `MedianParticipationTests`,
  `MarketParticipantsTests`, `CollectionSchedulingTests`,
  `EmployeeAccessSettingsTests` и `AccessV1Ap03UiTests` → 30/30 Passed;
- в прогон входят владение и изоляция зон, запрет управления Parser для
  zone-only сотрудника, принадлежность объявления зоне, повышенные роли,
  одиночные и массовые операции, общий флаг расчёта и migration round-trip;
- после финальной правки подписей и отображения Administrator/ProcurementHead
  повторный адресный Release-прогон прав зон, медианы и UI-контрактов → 10/10 Passed;
- `dotnet ef migrations has-pending-model-changes --project src/LandErp.Infrastructure --startup-project src/LandErp.Infrastructure --configuration Release --no-build`
  → модель соответствует последней миграции.

Миграция: `20261009085314_SearchGroupOwnership`; создана, но к production не
применялась. Новых таблиц нет, поэтому runtime grants не менялись.

Коммит и push: `5fc74d9` (`origin/main`).

## Этап 4. Экономика локаций

Реализовано:

- существующие независимые поля `DemandTestPricePerSotka` и
  `TargetPurchasePricePerSotka` переиспользованы без новой модели хранения;
- в обзоре и во входящих оба ориентира показаны рядом с однозначными подписями
  «Ориентир теста спроса» и «Желаемая цена покупки»;
- в полной карточке объекта и боковой карточке показаны все связанные зоны, а
  не только одна выбранная зона;
- для каждой связанной зоны рассчитана целевая стоимость участка по желаемой
  цене покупки за сотку и рабочей площади объекта;
- в блоке «Цена и торг» для цены объявления, цены продавца, нашего предложения
  и согласованной цены добавлено вычисляемое значение за сотку;
- производные значения не сохраняются: общие расчёты выполняются через
  `LocationEconomics`, с округлением до двух знаков;
- отсутствие цены и отсутствие либо нулевая площадь отображаются как разные
  явные состояния и не подменяются нулём;
- существующие чтение, сохранение, optimistic concurrency и аудит ручных
  ориентиров зон сохранены без изменений.

Изменённые файлы:

- `src/LandErp.Application/Modules/Overview/Public/GroupMarketContracts.cs`;
- `src/LandErp.Server/Components/Pages/Home.razor`;
- `src/LandErp.Server/Components/Pages/Home.razor.css`;
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor`;
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor.css`;
- `src/LandErp.Server/Components/Primitives/DemandTestPrice.razor`;
- `src/LandErp.Server/Components/Primitives/GroupMarketPrices.razor`;
- `src/LandErp.Server/Components/Primitives/GroupMarketPrices.razor.css`;
- `src/LandErp.Server/Components/Procurement/CaseWorkspace.razor`;
- `src/LandErp.Server/Components/Procurement/CaseWorkspace.razor.css`;
- `tests/LandErp.Foundation.Tests/MarketDemandTests.cs`;
- `tests/LandErp.Foundation.Tests/ProcurementQueueV2ReadTests.cs`.

Проверки:

- `dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore`
  → успешно, 0 ошибок, 0 предупреждений;
- Release-прогон `MarketDemandTests` и `ProcurementQueueV2ReadTests`
  → 10/10 Passed;
- тесты покрывают расчёт цены за сотку и целевой стоимости, округление,
  отсутствующую/нулевую/отрицательную площадь, сохранение обоих ориентиров и
  UI-контракты отображения всех связанных зон;
- визуальная приёмка владельцем не выполнялась в соответствии с ограничением
  активного Gate на Browser/CUA/Playwright.

Изменений схемы и новой migration нет.

Коммит и push: `52e08a3` (`origin/main`).

## Этап 5. Комментарии входящих объявлений

Реализовано:

- добавлены `catalog.comment_types`, `catalog.listing_comments` и
  `catalog.listing_comments_history` с организационной изоляцией и связями с
  объявлениями, сотрудниками и видами комментариев;
- для каждой пары объявления и вида действует уникальность
  `(organization_id, listing_id, comment_type_id)`; актуальные комментарии и
  виды защищены optimistic concurrency;
- существующие организации получили начальный вид «Общий комментарий», а
  bootstrap новой организации создаёт его вместе с Owner;
- PostgreSQL-триггер сохраняет прежнее значение при содержательном UPDATE и при
  DELETE; технический UPDATE без изменения текста историю не создаёт;
- автор UPDATE/DELETE передаётся через транзакционный
  `set_config('landerp.comment_actor_employee_id', ..., true)`; отсутствие
  контекста отклоняет содержательное прямое изменение, после транзакции значение
  не остаётся в соединении пула;
- сотрудники с уровнем Incoming `Process` могут создавать, изменять и очищать
  комментарии доступного объявления; ProcurementHead, Administrator и Owner
  управляют видами;
- вид комментария не удаляется физически: архивирование запрещает новые
  значения, но сохраняет текущие комментарии и всю историю;
- поиск по актуальному тексту комментариев включён в серверный предикат до
  пагинации и использует GIN full-text index;
- комментарии текущей страницы загружаются одним пакетным запросом, без запроса
  на каждую строку;
- во входящих добавлена одна компактная колонка «Комментарии» с количеством и
  превью; редактор поддерживает добавление, изменение, очистку, просмотр истории
  конкретного вида и управление справочником для уполномоченных ролей;
- все записи дополнены существующим audit организации; runtime grants обновлены
  только для трёх новых таблиц.

Основные изменённые файлы:

- `src/LandErp.Application/Modules/Catalog/Domain/CatalogModels.cs`;
- `src/LandErp.Application/Modules/Catalog/Public/ListingCommentContracts.cs`;
- `src/LandErp.Application/Modules/Catalog/Public/IncomingCatalogReadContracts.cs`;
- `src/LandErp.Infrastructure/Modules/Catalog/ListingCommentService.cs`;
- `src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogQuery.cs`;
- `src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogReadService.cs`;
- `src/LandErp.Infrastructure/Modules/Collection/CollectionMappings.cs`;
- `src/LandErp.Infrastructure/Modules/IdentityAccess/LocalBootstrap.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementServices.cs`;
- `src/LandErp.Infrastructure/Persistence/LandErpDbContext.cs`;
- `src/LandErp.Infrastructure/Persistence/ModelConventions.cs`;
- `src/LandErp.Infrastructure/Persistence/ProductionDatabaseInitializer.cs`;
- `src/LandErp.Infrastructure/Migrations/20261009094850_ListingComments.cs`;
- `src/LandErp.Infrastructure/Migrations/20261009094850_ListingComments.Designer.cs`;
- `src/LandErp.Infrastructure/Migrations/LandErpDbContextModelSnapshot.cs`;
- `src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor`;
- `src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor.css`;
- `tests/LandErp.Foundation.Tests/ListingCommentTests.cs`;
- `tests/LandErp.Foundation.Tests/PostgresTests.cs`.

Проверки:

- `dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore`
  → успешно, 0 ошибок, 0 предупреждений;
- адресный Release-прогон комментариев, чтения/фильтров входящих, мониторинга,
  участников расчёта и migration/runtime boundary → 30/30 Passed;
- отдельный адресный прогон `ListingCommentTests` → 3/3 Passed;
- отдельный migration/runtime-тест
  `RealPostgresMigrationsCommentsRuntimeIsolationAndRecovery` → Passed;
- `dotnet ef migrations has-pending-model-changes` в Release → модель
  соответствует последней миграции;
- первый черновой профиль по имени класса захватил один старый browser-тест: он
  остановился до запуска хоста и браузера из-за отсутствующего локального пути
  `artifacts/stage1/dotnet/dotnet.exe`; итоговый разрешённый профиль повторён с
  явным исключением этого метода и прошёл 30/30;
- Browser/CUA/Playwright и визуальная приёмка не выполнялись; она остаётся
  владельцу в соответствии с активным Gate.

Миграция: `20261009094850_ListingComments`; создана и проверена на изолированных
PostgreSQL-базах, но к production не применялась.

Коммит и push: `e71fb09` (`origin/main`).

## Этап 6. Адресные улучшения

Реализовано и подтверждено:

- рядом с кадастровым номером в основной карточке добавлено отдельное заметное
  действие `Исправить`;
- действие открывает существующую форму с уже выбранным кадастровым номером и
  использует существующую команду `CorrectCaseFactAsync`; отдельное сохранение,
  новая таблица или вторая история не добавлялись;
- сохранено разделение рабочего факта карточки и сведений источника, а прежние
  значение, причина, автор и время остаются в бизнес-истории и аудите;
- подтверждено, что обычный активный сотрудник без включённой MFA проходит
  password-only вход, а Owner/Administrator без MFA не получает
  привилегированный доступ и при первом входе направляется на настройку MFA;
- код входа и MFA соответствовал требованиям и не изменялся;
- найден и исправлен доказанный дефект формы сотрудника: скрытая системная роль
  ранее начиналась пустой и блокировала создание, пока администратор вручную не
  раскрывал дополнительный блок; теперь безопасной ролью по умолчанию является
  `Viewer`, а рабочий Access V1 по-прежнему начинается с `Нет доступа`;
- ФИО, логин и системная роль явно обозначены обязательными и получили понятные
  сообщения валидации; отдел, команда, должность и руководитель явно помечены
  необязательными;
- реальным PostgreSQL-тестом подтверждено создание сотрудника без отдела,
  команды, должности и руководителя, корректный временный пароль, отсутствие
  обязательной MFA у обычного сотрудника и сохранение явной конфигурации
  Access V1.

Изменённые файлы:

- `src/LandErp.Server/Components/Procurement/CaseWorkspace.razor`;
- `src/LandErp.Server/Components/Procurement/CaseWorkspace.razor.css`;
- `src/LandErp.Server/Components/Pages/OrganizationPage.razor`;
- `tests/LandErp.Foundation.Tests/UserRequestsStage6Tests.cs`;
- `reports/USER_REQUESTS_2026-10-05_08_REPORT.md`.

Проверки:

- `dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore`
  → успешно, 0 ошибок, 0 предупреждений; сборка включает изменённые Server,
  Infrastructure и Foundation Tests;
- адресный Release-прогон `UserRequestsStage6Tests` → 2/2 Passed;
- регрессионный Release-профиль `ReleasePackageB201Tests`,
  `IdentityOrganizationTests`, `EmployeeAccessSettingsTests`,
  `AccessV1Ap03UiTests`, `ProductionSetupTests` и `UserRequestsStage6Tests`
  → 19/19 Passed;
- `dotnet ef migrations has-pending-model-changes` в Release с допустимой
  фиктивной локальной строкой design-time → изменений модели нет;
- полная `dotnet build LandErp.slnx -c Release --no-restore` не прошла из-за
  существующего вне scope предупреждения-анализатора `CA1305` в
  `tests/LandErp.ParserSpike.Tests/CollectionFixesTests.cs:97`; изменённый
  Foundation/Server-контур после этого собран отдельно без ошибок и
  предупреждений;
- Browser/CUA/Playwright не запускались; визуальная и ручная приёмка остаётся
  владельцу.

Изменений схемы и новой migration нет.

Коммит и push: отдельный коммит этапа 6 в `origin/main`; точный SHA передаётся
вместе с этим отчётом после фиксации коммита.

## Итог Gate

Все шесть этапов активного Gate реализованы последовательно. Production,
production-БД и рабочий сервер не изменялись.

Коммиты этапов 1–5:

1. `abad324` — фильтры и сортировка очереди закупки;
2. `5a23790` — системное отклонение и возврат в канбане;
3. `5fc74d9` — зоны поиска и участие в медиане;
4. `52e08a3` — экономика локаций;
5. `e71fb09` — комментарии входящих объявлений.

Для отдельного production-развёртывания владельцу необходимо следовать
`docs/03-active/PRODUCTION_RUNBOOK.md`: обновить приложение до итогового SHA,
предварительно просмотреть SQL и явно применить миграции
`20261009083422_KanbanRejectionTarget`,
`20261009085314_SearchGroupOwnership` и
`20261009094850_ListingComments` миграционной ролью, применить runtime grants,
перезапустить Server/Worker и выполнить ручную/визуальную приёмку. Эти действия
в рамках текущего Gate не выполнялись.
