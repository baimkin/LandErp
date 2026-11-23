# INCOMING-FAST-OPEN-TRANSFER

20.11.2026. Gate: docs/03-active/GATE-INCOMING-FAST-OPEN-TRANSFER.md.
Прямой запрос владельца: сначала исправить тормоза открытия карточки входящего
и передачи менеджеру. Работа выполнена локально; production и его логи не читались.

## Доказанный избыточный путь и результат

Открытие: OpenDetailAsync → RegisterViewedForUiAsync → ExecuteConfirmedAsync
с прежним общим ReadAsync (все фасеты/справочники/market) → ReadDetailAsync.
Передача: ExecuteConfirmedAsync(TransferToProcurementAsync) → тот же общий read
→ отдельное чтение открытой карточки. Оба целевых сценария теперь обходятся без
общего IncomingReads.ReadAsync / Markets.ReadIncomingAsync. Это проверено запретом
таких вызовов в тестах; production latency и причина screenshot incident
01a1f951-dafb-7209-bf62-431a9dbeeaf8 не объявляются измеренными/установленными.

## Изменения

- Additive read contracts: ReadItemsAsync для максимум 100 явных ID и
  ReadChangeScopeAsync для источника/подтверждённой группы и matching IDs текущего
  отбора. Существующий RequireRead/organization guard и IncomingCatalogSelection
  переиспользованы. Полного count/facet/group-reference/market read здесь нет.
- Общая проекция строк сохранена; ReadItems сбрасывает presentation Offset,
  ограничивает ID до сортировки/Take и не выполняет остальные page queries для
  пустого результата. Фактическая принадлежность New/пользовательским отбором
  определяется сервером, а не предположением клиента.
- Просмотр по-прежнему регистрирует событие и аудит. Подтверждённая запись
  обновляет строку через ReadItems, либо исключает её и восполняет страницу.
  Читающий сотрудник не выполняет запись просмотра. Родительский Loading не
  переключается при открытии: данные списка и таблица остаются на месте.
- Отдельный loading/error в drawer, request sequence и cancellation для чтения.
  Закрытие/новая карточка/dispose не допускают перезаписи более новым ответом
  старого запроса. Повтор после ошибки читает только карточку; новую бизнес-запись
  не отправляет. Неопределённая отметка просмотра не объявляется подтверждённой.
- Перед передачей выполняется небольшое чтение scope группы. Ошибка на этом этапе
  прямо говорит, что запись не отправлена, и сохраняет получателя. Команда передачи
  и её проверки прав/получателя/дублей/история не менялись.
- После acknowledged commit модальное окно закрывается и известная связь с кейсом
  отмечается локально до дополнительных reads. Затем before/after membership группы
  корректирует Total, включая источники за пределами страницы. Затронутый явный
  выбор инвалидируется; прочий выбор и режим «все минус исключения» сохраняются.
- Для обычного Incoming удаляются только затронутые строки и читается недостающее
  окно. Если отбор сохраняет/получает источники, читается только текущая страница
  для корректного порядка. Общий refill используется и предыдущим QuickDismiss.
- Сбой post-commit read сохраняет Committed/RefreshRequired и известную связь с
  кейсом; повторная передача не предлагается. Unknown/Rejected не удаляют строку
  и выбор. Успех не остаётся в плавающем toast поверх кнопок.
- Позиции страницы/таблицы сохраняются существующим безопасным JS-модулем.
  Сводные счётчики/market остаются снимком до обычного фильтра/навигации/reload.

## Файлы текущей правки

- src/LandErp.Application/Modules/Catalog/Public/IncomingCatalogReadContracts.cs
- src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogReadService.cs
- src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor
- src/LandErp.Server/Components/Pages/IncomingCatalogV2.Updates.cs
- src/LandErp.Server/Components/Pages/IncomingCatalogV2.Dismiss.cs — общий refill rename.
- tests/LandErp.Foundation.Tests/IncomingFastOpenTransferTests.cs
- tests/LandErp.Foundation.Tests/IncomingFastOpenTransferBrowserTests.cs
- tests/LandErp.Foundation.Tests/IncomingQuickDismissTests.cs — очистка счётчика
  измерения после нового локального refill при открытии New.
- docs/14-ui-kit/screens/01-incoming-listings.md; Gate/ACTIVE_TASK; этот отчёт.

## Фактические проверки

```powershell
dotnet restore tests/LandErp.Foundation.Tests --locked-mode --artifacts-path artifacts/incoming-fast-open-transfer/build
dotnet build tests/LandErp.Foundation.Tests -c Release --no-restore --artifacts-path artifacts/incoming-fast-open-transfer/build --disable-build-servers -p:UseSharedCompilation=false
$env:LANDERP_DOTNET = (Get-Command dotnet).Source
$env:LANDERP_TEST_BUILD_ROOT = (Resolve-Path artifacts/incoming-fast-open-transfer/build).Path
dotnet test tests/LandErp.Foundation.Tests -c Release --no-restore --artifacts-path artifacts/incoming-fast-open-transfer/build --disable-build-servers -p:UseSharedCompilation=false --filter 'FullyQualifiedName~IncomingFast|FullyQualifiedName~QuickDismissInNewSlice|FullyQualifiedName~IncomingManagerTransferBrowserTests|FullyQualifiedName~IncomingQuickDismissBrowserTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=incoming-fast-regression.trx' --results-directory artifacts/incoming-fast-open-transfer/tests
```

- Locked restore и Release Passed. Исходный code build — 0 warnings/errors;
  новый test query первоначально потребовал явной проверки nullable Title,
  после её добавления сборка/тесты прошли без ошибок.
- **9 Passed, 0 Failed/Skipped**, 5m22s всего (setup/migrations/fixtures/browser
  включены, это не время одного действия интерфейса).
- 25/50/100 строк: группа из двух источников на разных страницах, Total уменьшен
  на 2, refill одной строки либо clamp пустой последней страницы, неизменный
  выбор/исключения и порядок, назначенный менеджер и одна созданная карточка.
  Во время целевых действий full-list/market вызовы запрещены: попыток 0.
  Проверена изоляция чужой организации в новых reads.
- Старый read, намеренно игнорирующий cancellation, не подменяет новую карточку.
  Ошибка чтения/повтор не регистрируют просмотр ещё раз. Read-only не получает
  обработку и не создаёт отметку просмотра.
- Preflight failure не отправляет запись; Rejected/Unknown сохраняют получателя
  и строку; после настоящего commit + failed read известная связь сохраняется,
  обычный reload не повторяет команду и показывает правильное количество.
- Новый real-browser тест: локальная PostgreSQL/Server/Edge. Искусственная
  блокировка только тестовой строки показала loading drawer до завершения write;
  закрытие не открывает карточку позже, аудит/первый просмотр сохранены.
  Переданы два источника, включая скрытый на следующей странице; 27 → 25,
  та же DOM-таблица, сохранены вертикальная/горизонтальная прокрутки, «Далее»
  отключена, выбранный получатель назначен. JS errors 0. Скриншоты loading и
  transferred-group на 1440px просмотрены; новую mobile-приёмку не заявляем.
- Два существующих browser регресса (права передачи и QuickDismiss/pagination)
  и New-slice regression Passed. Никаких внешних source calls не выполнялось.
- Evidence: artifacts/incoming-fast-open-transfer/tests/incoming-fast-regression.trx;
  browser/card-loading-1440.png, transferred-group-1440.png. Тестовые host/browser
  остановлены. Whitespace check Passed.

## Ограничения / следующий шаг

Это адресные проверки, не полный foundation suite. Обычная первичная загрузка,
смена фильтров/страниц и явный reload сохраняют полный read. Сохранённые фильтры
по-прежнему разрешаются сервером; Participants использует прежнее определение
участников. Нулевого количества SQL и улучшения всех возможных detail queries
не заявляем. Конкурентные внешние изменения могут потребовать обычного обновления;
длительный серверный snapshot выдачи не вводился.

Команды/права/schema/packages/Parser не менялись. Работающий preview не
перезапускался; прежние Logging/Parsing/Incoming изменения, ветка и индекс
сохранены. Commit/push/production не выполнялись. Gate локально завершён.
Следующий шаг — отдельно согласованная публикация/серверная проверка двух действий;
конкретный production read failure расследуется по его логу, а не по общему тексту.
