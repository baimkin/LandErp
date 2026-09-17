# Parser Workspace UX — отчёт

Дата: 17 сентября 2026. Gate: `GATE-PARSER-WORKSPACE-UX.md`. Ветка: `codex/parser-workspace-ux`.

## Результат

- Четыре раздела: **Поиски**, **Работа**, **Результаты**, **Настройки**. Общая WPF-тема, крупные элементы, группы слева, человеческие состояния и сообщения.
- Одна форма создания и редактирования поиска: название, ссылка, группа, ручной запуск / интервал / время суток. Локальная ссылка и расписание сохраняются одной транзакцией.
- В серверном режиме формы работают напрямую с серверными группами и поисками. Создание, редактирование, включение/приостановка и постановка в очередь проверяют право `CanManageSearches`. Изменения используют версии записей и аудит. Повторная постановка не создаёт ещё одно активное задание.
- Локальная ссылка добавляется на сервер только отдельной кнопкой, с обязательным выбором серверной группы и подтверждением. Исходное локальное расписание, группа, история и результаты не копируются. В окне задаётся новое серверное расписание; исходный локальный поиск остаётся.
- «Открыть сайт» позволяет настроить поиск в браузере; «Добавить из браузера» открывает форму сразу для выбранного режима. Отдельная локальная заготовка в серверном режиме не создаётся.
- Авторабота включается явно, состояние сохраняется. «Остановить всё» выключает её и останавливает текущий сбор. Учтена остановка во время получения серверного задания. Переключение режима выключает автоработу, не переносит данные.
- Восстановление сохранённого подключения и повторение запросов при временном отсутствии связи. Окончательно отклонённые сервером результаты остаются локально; они не удерживают следующие задания в бесконечной доставке. При открытой форме продолжается обслуживание текущего серверного задания, запуск нового отложен.
- Поддерживаются нынешние одноразовые коды `LDP1` и прежние коды/сохранённые подключения. Выданный постоянный ключ защищён Windows DPAPI. Технические поля подключения убраны.
- Результаты и история разделены по режимам; карточка показывает последнее наблюдение вместо JSON. Экспорт CSV учитывает текущий текстовый фильтр и режим, экранирует формулы. Краткая диагностика сохраняется в файл без ключей, URL и сырых ответов.
- Удалены старый исследовательский `MainWindow`, его обработчики, таблица `SettingsEditor` и тесты удалённого экрана. Из рабочего приложения убрана запись диагностических JSON-ответов. Сам сбор данных, адаптеры, профили браузеров, история и внутренние средства диагностики ядра сохранены.

## Изменённые файлы этого Gate

- `src/LandErp.ParserSpike.Desktop/`: `App.xaml`, `WorkspaceWindow.xaml`, `WorkspaceWindow.xaml.cs`, `WorkspaceController.cs`, `ServerConnectionWindow.xaml`, `ServerConnectionWindow.xaml.cs`; добавлены `ParserTheme.xaml`, `SearchEditorWindow.cs`; удалены `MainWindow.xaml`, `MainWindow.xaml.cs`, `SettingsEditor.cs`.
- `src/LandErp.ParserSpike/LocalCollection/`: `LocalStore.cs`, `Models.cs`.
- `src/LandErp.ParserSpike/ServerIntegration/`: `ServerAdapter.cs`, `ServerCoordinator.cs`, `ServerOutbox.cs`.
- `src/LandErp.Collector.Contracts/V1/CollectorControlContracts.cs`.
- `src/LandErp.Application/Modules/Collection/Public/CollectionServices.cs`.
- `src/LandErp.Infrastructure/Modules/Collection/CollectorGateway.cs`.
- `src/LandErp.Server/Foundation/CollectorEndpoints.cs`.
- `tests/LandErp.ParserSpike.Tests/`: `WorkspaceUiTests.cs`, `LocalCollectionTests.cs`, `ServerTransportTests.cs`, `AvitoMapTests.cs`; удалён `DesktopUiTests.cs` для удалённого экрана.
- `tests/LandErp.Foundation.Tests/CollectionPoolTests.cs`.
- `Start-Parser.cmd`, `scripts/Start-Parser.ps1` — запуск с локальным SDK, сборка в отдельную папку.
- `docs/03-active/ACTIVE_TASK.md`, `GATE-PARSER-WORKSPACE-UX.md`, этот отчёт.

Параллельные изменения Server UI, CSS, UI-kit и иных экранов уже присутствовали/появлялись в рабочем дереве отдельно. Они не являются результатом Parser Gate и не откатывались. Новых пакетов и миграций нет. Пользовательская база и профили не изменялись проверками.

## Проверки и команды

SDK: `artifacts/stage1/dotnet/dotnet.exe` (.NET 10.0.112).

1. `build src/LandErp.ParserSpike.Desktop/LandErp.ParserSpike.Desktop.csproj -c Release --no-restore -o artifacts/parser-workspace/parser` — успешно, 0 предупреждений/ошибок.
2. `build src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore -o artifacts/parser-workspace/server -p:CopyRetryCount=0` — успешно, 0 предупреждений/ошибок; сборка в отдельную папку, поскольку рабочие серверные файлы заняты запущенным процессом.
3. Offline Parser suite: `test tests/LandErp.ParserSpike.Tests -c Release --no-build --filter "FullyQualifiedName!~LocalBrowserTests&FullyQualifiedName!~SpaPaginationWaitsForNewCardsWithoutDocumentNavigation&FullyQualifiedName!~HeadedBrowserScrollLoadsDomAndClicksNextWithNoLiveRequests&FullyQualifiedName!~MapWheelScrollsRightContainerCapturesJsonAndDoesNotTouchMap&FullyQualifiedName!~VisibleChromeReadsOnlyMainCardsAndStopsAtOtherRegions"` — **89/89**, итог в `artifacts/parser-workspace/tests/parser-offline.trx`. Включает WPF Local/Server, атомарное сохранение, разделение результатов, остановку/рестарт, гонку stop/claim, истёкшее задание, коды подключения, CSV, существующие runtime/storage/adapter tests.
4. `test tests/LandErp.Foundation.Tests -c Release --no-restore -o artifacts/parser-workspace/foundation-tests --filter FullyQualifiedName~CollectionPoolTests` — **5/5**, отдельные одноразовые PostgreSQL базы. Evidence: `artifacts/parser-workspace/tests/parser-gateway.trx`.
5. WPF окна действительно открывались в тестах. Снимки четырёх вкладок сохранены как `artifacts/parser-workspace/workspace.0.png` … `.3.png`; проверены отображение, переносы подсказок и изменение размера. Серверный UI использует фиктивный HTTP transport; gateway отдельно проверен на PostgreSQL.
6. PowerShell parser проверил синтаксис `scripts/Start-Parser.ps1`. `git diff --check` — успешно.

Первый расширенный запуск выявил ошибку старой migration fixture: она понижала номер схемы до v1, оставляя таблицы v3. Исправлена только подготовка одноразовой базы в тесте; рабочая миграция не менялась. DPAPI недоступен в sandbox-контексте: серверный WPF тест успешно повторён под обычным Windows-пользователем с фиктивным ключом и сервером; шифрование не ослаблялось.

## Запуск и границы

Закрыть прежний Parser и запустить `Start-Parser.cmd` из корня репозитория. Он соберёт актуальное приложение и откроет его через доступный .NET SDK. Данные берутся из прежней `local-data/spike-002.sqlite`, браузерные профили — из прежней `browser-profiles`.

Запущенный рабочий Server не останавливался и не обновлялся. Для новых серверных команд требуется запуск обновлённой серверной сборки штатным способом с прежней конфигурацией. Production apply, commit, merge и push не выполнялись.

Ограничения: реальные Авито/Циан и полный browser suite в этом Gate не запускались; live CAPTCHA/авторизация и сохранение конкретного полигона требуют проверки на сайте. Геометрия по-прежнему зависит от ссылки источника; отдельный контракт переноса полигона не добавлен. Серверный поиск приостанавливается, локальный может архивироваться; серверные группы архивируются. Расписания на компьютере требуют открытого приложения и включённой автоработы. CSV отражает последние наблюдения выбранного режима, полная история остаётся в базе.

Следующий шаг: визуальная приёмка новой версии владельцем и короткая проверка живого сценария «карта → добавить поиск → группа → расписание» после запуска обновлённого Server. Другой Gate не активирован.
