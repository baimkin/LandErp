# Передача входящего объявления менеджеру

16.11.2026. Gate INCOMING-MANAGER-TRANSFER. Реализовано и проверено локально.

Внизу карточки входящего объявления добавлена кнопка «Передать менеджеру».
Используется существующий список допустимых получателей и штатная команда
TransferToProcurementAsync. Выбранный сотрудник становится ответственным за новый
PropertyCase и его текущим исполнителем. Отправитель остаётся автором передачи.
Существующие права, бизнес-история, Audit и защита от повторного создания не менялись.

В форме показаны объект, последствия передачи, при необходимости — передача
подтверждённой группы источников вместе. Получатель не выбирается автоматически.
До выбора подтверждение отключено; предусмотрены загрузка, пустой список,
ошибка чтения с повтором и ошибка команды внутри формы. Поздний ответ чтения
не восстанавливает закрытую форму. Для уже связанного объявления кнопка скрыта.
«Взять в работу» остаётся у сотрудника с соответствующим прежним доступом.

## Файлы

- `src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor` — кнопка и форма;
- `tests/LandErp.Foundation.Tests/IncomingManagerTransferBrowserTests.cs`;
- `docs/14-ui-kit/screens/01-incoming-listings.md`;
- `docs/03-active/GATE-INCOMING-MANAGER-TRANSFER.md`, запись ACTIVE_TASK;
- этот отчёт. Backend, стили, схема и пакеты не изменялись.

## Проверки

`dotnet build tests/LandErp.Foundation.Tests -c Release --no-restore --disable-build-servers -p:UseSharedCompilation=false`:
0 warnings/errors. Shared compilation отключена после блокировки выходной DLL
компилятором; последующая обычная изолированная компиляция прошла.

`dotnet test tests/LandErp.Foundation.Tests -c Release --no-build --no-restore --filter "FullyQualifiedName~IncomingManagerTransferBrowserTests|FullyQualifiedName~IncomingOnlyTransfersToEffectiveProcurementRecipientWithoutGainingProcurement"`:
существующая backend-проверка прав прошла; первый browser-run требовал настройки
сценария на «Все объявления», поскольку fixture не имеет сохранённых фильтров.
После этой коррекции отдельно запущен IncomingManagerTransferBrowserTests — Passed.
Итог: обе адресные проверки прошли, весь foundation suite не запускался.

Браузер работал с настоящим локальным Server и одноразовой PostgreSQL БД.
Подтверждено: оператор только входящих видит передачу без собственного права
закупки; явный выбор; ManagerEmployeeId и WorkAssignment получателя; автор и
получатель в истории, одна запись Audit; повтор не создаёт дубль; передача не
выдаёт отправителю доступ к закупке; отзыв прав у получателя после выбора
блокирует команду без создания объекта; read-only не видит кнопку; сотрудник
с обработкой входящих и работой в закупке видит обе кнопки.
JavaScript errors — 0. Проверены 1440 и 390 px, переполнения документа нет.
Снимки обеих форм просмотрены. Для HTTPS тест запускался с разрешённым доступом
Windows к ключу временного сертификата. Секреты/соединения не выводились.

Evidence: `artifacts/incoming-manager-transfer/tests/incoming-manager-transfer.trx`,
`incoming-manager-transfer-browser.trx`; снимки `select-manager-1440.png` и
`select-manager-390.png`. `git diff --check` затронутых tracked файлов прошёл.

## Границы

Массовая передача не входит в этот запрос. Отдельное закрепление объявления
за менеджером без создания PropertyCase не добавлялось. Параллельные изменения
сохранены. Production, commit/push, новые migrations и новые зависимости отсутствуют.
Следующий шаг — публикация и обновление сервера по отдельному запросу владельца.
