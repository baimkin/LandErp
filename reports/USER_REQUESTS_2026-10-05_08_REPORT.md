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

Коммит и push: фиксируются отдельным коммитом этапа 3; SHA будет добавлен в
отчёт при фиксации следующего этапа.

## Этапы 4–6

Не начаты на момент фиксации этапа 3.
