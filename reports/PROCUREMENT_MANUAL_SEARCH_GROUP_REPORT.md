# Ручная группа поиска объекта закупки

Дата: 21.10.2026. Ветка: `codex/procurement-manual-search-group`.
Исходный HEAD: `eb2e697764368656d04b8a099d18c8e8bb51c8e0`.
Основание: прямое согласие владельца реализовать простой выбор группы только
для объектов закупки, включая ранее созданные вручную; показатели карточки
и состав медианы не менять. Приоритет ручного выбора и сохранение архивной
связи отдельно согласованы в текущем чате.

## Результат и решения

- В форме ручного создания есть необязательная группа поиска. Назначение/смена/
  снятие доступны также в существующей полной и боковой карточке через один
  общий редактор. Разрешение следует из текущего права редактировать досье;
  право управления парсерами/создания групп для назначения не требуется.
- `PropertyCase.ManualSearchGroupId` — одна nullable-ссылка на существующую
  группу. Таблиц назначения, фоновых заданий и новых зависимостей нет.
- При заданной ссылке только выбранная действующая группа определяет показатели
  объекта и его попадание в фильтр группы. Без неё сохраняется прежний вывод по
  подтверждённым источникам и наблюдениям. Общая выборка используется и карточкой,
  и фильтром, поэтому при совпадении с группами источников объект не дублируется.
- Архивирование сохраняет ссылку. Редактор показывает имя и пометку архива;
  группы источников автоматически не подставляются. Выбрать заново можно только
  действующую группу своей организации. Снятие ручного выбора возвращает вывод
  по источникам. Архивные группы, как прежде, не выдают текущие показатели.
- Сервер проверяет права, рабочую видимость объекта, организацию и активность
  группы, ExpectedCaseVersion. Изменение сохраняет audit и business timeline
  одной транзакцией. Повтор без содержательного изменения не увеличивает версию
  и не создаёт историю. Общая организационная блокировка сериализует команды;
  блокировка группы защищает проверку активности от параллельного архивирования.
- В асинхронном редакторе CaseId, версия и выбранная группа фиксируются до await;
  результат сохранения не относится к другой открытой карточке.
- Текущие компоненты показателей, их состав, формулы и участники медианы не
  изменены. Назначение не создаёт Catalog item/Job/Observation и не меняет цену
  источника. Ручные входящие предложения и Collector не затронуты.
- У нового необязательного поля DTO при null подавлена JSON-сериализация:
  отпечатки старых команд ручного создания и повторная доставка совместимы.

## Файлы

- `docs/03-active/ACTIVE_TASK.md` — текущая утверждённая единица работы.
- `docs/14-ui-kit/screens/02-procurement-queue.md` — согласованное поведение.
- `src/LandErp.Application/Modules/Procurement/Domain/PropertyCase.cs` — ссылка.
- `src/LandErp.Application/Modules/Procurement/Public/ProcurementContracts.cs` —
  выбранная группа в чтении/создании, команда смены и метод workspace.
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementMappings.cs` — FK,
  Restrict и комментарий столбца.
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementCaseSearchGroup.cs`
  — команда назначения и общая проверка группы.
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs` —
  группа при создании и чтении карточки.
- `src/LandErp.Infrastructure/Modules/Overview/GroupMarketService.cs` —
  приоритет явной ссылки в общей выборке принадлежности к группам.
- `src/LandErp.Infrastructure/Migrations/20261021063141_ProcurementManualSearchGroup.cs`
  и `.Designer.cs`, `LandErpDbContextModelSnapshot.cs` — новая схема.
- `src/LandErp.Server/Components/Procurement/CaseSearchGroupPicker.razor` и
  `.razor.css` — общий редактор на текущих UI tokens.
- `src/LandErp.Server/Components/Procurement/CaseWorkspace.razor` и
  `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor` — подключение
  редактора и поле формы создания.
- `tests/LandErp.Foundation.Tests/ProcurementSearchGroupTests.cs` — пять
  новых сценариев, включая настоящий Razor-компонент через HtmlRenderer.
- Этот отчёт.

## Миграция

`20261021063141_ProcurementManualSearchGroup`: nullable UUID-столбец
`procurement.property_cases.manual_search_group_id`, индекс и FK на
`collection.search_groups(id)` с DeleteBehavior.Restrict. Существующие строки
получают null и сохраняют прежнее поведение; backfill не требуется.

Runtime уже имеет SELECT/INSERT/UPDATE на `procurement.property_cases` и SELECT
на группы. Новых grants не требуется; назначение проверено через runtime-role
изолированной БД. Startup и production provisioning не изменены. Миграция
применялась только в принадлежащих тестам disposable PostgreSQL-базах.

## Фактические проверки

1. `dotnet build src/LandErp.Infrastructure/LandErp.Infrastructure.csproj -c Release --no-restore`
   — Passed, 0 warnings/errors.
2. `dotnet ef migrations add ProcurementManualSearchGroup --project src/LandErp.Infrastructure --configuration Release --no-build`
   — Passed. Использована отдельная фиктивная design-time строка подключения
   к имени `landerp_test_design_manual_group`; подключение/изменение рабочей БД
   не выполнялось. Первый вызов с `-c Release` был отклонён: этот флаг означает
   DbContext; исправлено на `--configuration Release`.
3. `dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore`
   — Passed, 0 warnings/errors; собраны Application, Infrastructure, Server,
   Worker и зависимости тестового проекта.
4. `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProcurementSearchGroupTests|FullyQualifiedName~ContextsShareWholeGroupPricesAndCasesUseConfirmedVisibleSourceMembership|FullyQualifiedName~CleanDatabaseMigrationAppliesWithoutLegacyBackfillContract" --logger "console;verbosity=normal" --logger "trx;LogFileName=procurement-search-group.trx" --results-directory artifacts/tests/procurement-search-group`
   — Passed, 7/7, 0 Failed, общий завершённый запуск около 1 мин 56 с.
5. `git diff --check` — Passed.

Подтверждены:

- создание с группой и без неё; назначение/смена/снятие у существующего объекта;
  версии, no-op, audit/история и повтор старых/новых команд создания;
- совпадение показателей группы для ручного объекта и общего чтения, включая
  тест спроса/желаемую цену; неизменность медианы, участников и Catalog;
- явная группа поверх источников, снятие и восстановление прежнего поведения,
  фильтрация без дублей, архив без автоматического fallback;
- чужая/несуществующая/архивная группа, чужой/невидимый объект и режим чтения
  отклоняются; два одновременных изменения имеют одного победителя;
- миграция с предыдущей схемы с сохранением номера, фактов и версии объекта;
  чистая схема, отсутствие pending model changes, запись runtime-role;
- настоящий общий редактор через HtmlRenderer: выбор/сохранение, архивная
  пометка, снятие назначения и отсутствие редактирования в режиме чтения;
- существующая проверка групп из подтверждённых источников остаётся зелёной.

Ignored evidence: `artifacts/tests/procurement-search-group/procurement-search-group.trx`.

## Ограничения и следующий шаг

Полный тестовый набор, браузер/CUA/Playwright и визуальная приёмка не выполнялись.
Форма создания и подключения редактора собраны; интерактивный сценарий редактора
проверен без браузера. Production, рабочая БД и работающий сервер не изменены.
Новые пакеты не добавлены. Пользовательские `.codex-remote-attachments/` не затронуты.
На этапе реализации commit/push/merge не выполнялись.

21.10.2026 владелец отдельно разрешил общий коммит этой доработки и компактных
строк ориентиров, затем отправку в `origin/main`. Этот отчёт, миграция и тесты
входят в публикацию. Вложения и локальные артефакты исключены. Фактический SHA
и удалённый main проверяются после push и сообщаются владельцу в чате.

Для установки потребуется отдельно разрешённое обновление приложения с явным
применением этой миграции. После обновления владельцу проверить выбор группы
при создании и у старого ручного объекта в полной/боковой карточке. Следующая
задача не активируется автоматически.
