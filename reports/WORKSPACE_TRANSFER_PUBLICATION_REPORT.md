# Публикация поисков/парсинга и передачи менеджеру

16.11.2026. По прямому запросу владельца подготовлена публикация в main двух
доработок: PARSING-WORKSPACE и INCOMING-MANAGER-TRANSFER. База удалённого main —
edce4b916b9aacc96c5c636cae8731ce567a15bc (галерея осмотра).

Включены четыре вкладки поисков, доказанные причины и inline результаты,
правка/повтор через реальные команды, отдельное чтение текущего задания;
кнопка передачи входящего выбранному менеджеру с прежними правами и историей.
Исходный HTML v2 сохранён как reference. Код других локальных задач не включён.
Публикуемый ACTIVE_TASK сохраняет историю main и только текущие два этапа.

Точный main-based snapshot содержит 24 файла двух задач и их документации.
Остальные файлы базы сравнили с archive main: незапланированных изменений 0.
Галерея уже содержится в base main; все пять её файлов совпадают с локальной
версией и входят в итоговую полную сборку, их не откатывали и не исключали.

Locked restore прошёл после разрешённого доступа к NuGet для штатной проверки
уязвимостей. Release build Foundation.Tests/Server/Worker — 0 warnings/errors.
В точном snapshot прошли 35/35 адресных проверок, 0 Failed: ParsingWorkspace,
CollectionSearchForm, CollectionScheduling, CollectionPool, передача входящего
(backend + настоящий браузер/PostgreSQL), два component-теста галереи.
Браузерные сценарии обеих функций прошли на одноразовых БД; production не вызывался.
Evidence: snapshot/artifacts/publication-tests/publication-verified.trx.
PowerShell syntax pinned update template — Passed. Сам deploy на сервере не запускался.

Фактический SHA commit и сверка blobs сохраняются отдельно в
artifacts/workspace-transfer-publication/verification.json, чтобы отчёт не
ссылался на ещё не существующий собственный SHA.

Скрипт обновления будет привязан к опубликованному коммиту и использовать
существующие Publish-Production/Deploy-ProductionRelease. Versioned publish
остаётся в C:\LandErp\artifacts\production\releases\<версия>, установленный
release — в C:\Program Files\LandErp\releases\<версия>. Private config, файлы
и БД не меняются. Новых migrations/packages нет. Production агентом не выполняется.
