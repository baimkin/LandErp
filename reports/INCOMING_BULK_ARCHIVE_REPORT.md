# Массовая отправка входящих объявлений в архив

Дата: 15.10.2026. Основание: прямой запрос владельца.
Ветка: `main`. Исходный HEAD: `8af0c894e53a4087cc776bb00fe8436b786b9173`.

## Scope и результат

Добавлено действие «В архив…» для выбранных строк входящих. Можно выбрать
страницу или все результаты текущего отбора между страницами и снять выбор
с отдельных строк. Перед записью показываются количества, причина,
необязательный комментарий и галочка «Также исключить из медианы».

«В архив» использует существующую классификацию «Не подходит» (`Dismissed`).
Специальные состояния уже архивных объявлений сохраняются. Данные, наблюдения,
история и связи источников не удаляются. Связанный `PropertyCase` не отклоняется,
его бизнес-стадия и канбан не меняются.

При доступной выбранной зоне галочка исключения из медианы включена по умолчанию.
При снятой галочке участие в медиане сохраняется. Без права на медиану зоны
галочка недоступна с пояснением, но обработка входящих позволяет архивировать.
Исключение меняет общий `IncludeInCalculation` во всех зонах.

## Реализация и границы

- Общий серверный отбор переиспользует предикаты страницы/сохранённых фильтров,
  текст, зоны и состояния; пагинация не ограничивает массовую операцию.
- Явный выбор передаёт ID и версии; режим «все» передаёт отбор и исключённые ID.
- Права обработки входящих, организация, право на медиану и реальная
  принадлежность каждого источника зоне проверяются сервером пакетно.
- Предпросмотр возвращает отпечаток ID/версий и состояния галочки. Перед записью
  строки блокируются в стабильном порядке и версии сверяются повторно.
- Классификация, выключение медианы, история и Audit сохраняются одной транзакцией.
- Одиночная и массовая классификации используют один существующий механизм записи.
- Новых пакетов, изменений схемы, миграций и runtime grants нет.
- Парсер/Collector и их источники не изменялись; тесты используют только локальные
  синтетические данные и серверную границу приёма результатов.

## Файлы

- `src/LandErp.Application/Modules/Catalog/Public/CatalogArchiveContracts.cs`.
- `src/LandErp.Infrastructure/Modules/Catalog/CatalogArchiveService.cs`.
- `src/LandErp.Infrastructure/Modules/Catalog/CatalogBulkSelectionReader.cs`.
- `src/LandErp.Infrastructure/Modules/Catalog/CatalogCalculationService.cs`.
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs`.
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementServices.cs`.
- `src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor`.
- `tests/LandErp.Foundation.Tests/CatalogBulkArchiveTests.cs`.
- `tests/LandErp.Foundation.Tests/CatalogBulkArchiveUiTests.cs`.
- `docs/03-active/ACTIVE_TASK.md`.
- `docs/14-ui-kit/screens/01-incoming-listings.md`.
- Этот отчёт.

## Проверки

Проверки фактически выполнены:

1. `dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore`
   — Passed, 0 warnings / 0 errors; собраны Application, Infrastructure, Server,
   Worker и тестовый проект на SDK `10.0.401`.
2. `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~CatalogBulkArchiveTests|FullyQualifiedName~MedianParticipationTests|FullyQualifiedName~IncomingArchiveSortingTests" --logger "trx;LogFileName=bulk-archive.trx" --results-directory artifacts/tests/bulk-archive`
   — 15/15 Passed, 0 Failed, 0 Skipped. Этот прогон выполнен до добавления
   дополнительного теста отката; шесть новых сценариев архива, семь существующих
   сценариев медианы и два существующих сценария архива/сортировки.
3. После добавления теста отката и двух проверок страницы повторная Release-сборка
   того же проекта — Passed, 0 warnings / 0 errors.
4. `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DatabaseFailureRollsBackArchiveMedianAndHistoryTogether|FullyQualifiedName~CatalogBulkArchiveUiTests" --logger "trx;LogFileName=bulk-archive-ui-rollback.trx" --results-directory artifacts/tests/bulk-archive`
   — 3/3 Passed, 0 Failed, 0 Skipped. Реальный отказ PostgreSQL с SQLSTATE `P0001`
   откатил классификацию, медиану и историю. `HtmlRenderer` выполнил обработчики
   настоящей Razor-страницы с выбранной зоной/галочкой и без зоны, проверив
   предпросмотр, переключение галочки, закрытие окна, очистку выбора и записи БД.
5. `git diff --check` — Passed. Проверен полный список изменённых/новых файлов:
   исходники парсеров/Collector, пакеты и migrations отсутствуют.

Итого 18 различных адресных проверок прошли в двух прогонах (15 + 3), это
не один общий запуск. PostgreSQL использовалась только локальная: sandbox
создаёт собственные уникальные `landerp_test_*` и удаляет только их.
Строка подключения прочитана из существующей переменной окружения без вывода.
TRX находятся в ignored `artifacts/tests/bulk-archive/`.

Покрываемые сценарии: отдельные ID и все результаты между страницами;
сохранённый фильтр и текст; снятые строки; оба состояния галочки;
история/Audit; права обработки и медианы, смена владельца зоны;
изоляция организаций; старые версии и изменение состава; сохранность
связанного объекта закупки и специальных архивных классификаций;
полный откат при синтетической ошибке PostgreSQL; повторное поступление
наблюдения без возврата из архива; реальные обработчики Razor-страницы
в `HtmlRenderer` без браузера и JavaScript.

## Ограничения и следующий шаг

Production, рабочая БД и работающий сервер не обновлялись. Браузер, CUA,
Playwright и визуальные проверки не использовались согласно активной задаче.
Ручная приёмка владельцем после отдельного обновления приложения: выбрать зону,
найти «Чувашия», проверить найденные строки, выбрать нужные или все результаты,
нажать «В архив…», выбрать «Локация» и проверить галочку и количества.

Архивация рабочих объявлений пользователя не выполнялась. Физическое удаление
не добавлялось. На этапе реализации commit/push и развёртывание не выполнялись. После завершения этого scope новая задача не запускается.

## Разрешённая отправка в GitHub — 19.10.2026

Владелец отдельно разрешил зафиксировать накопленные изменения массового архива
и пагинации и отправить их в `origin/main` для самостоятельного обновления сервера.
Этот отчёт входит в общий коммит обеих доработок. Фактические SHA локального
коммита и удалённого `main` проверяются после push и указываются в итоговом ответе.
Пользовательские скриншоты, credentials, local-data и TRX не публикуются.
Парсеры, схема БД, миграции и production не изменяются.