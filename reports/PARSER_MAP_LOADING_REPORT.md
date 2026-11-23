# PARSER-MAP-LOADING — 05.11.2026

## Evidence и решение

Апрелевка, job 04310b75-489b-4eee-b497-e8724ab1fe7b, 14:43 МСК:
сохранённый HTML содержит map-view с loading-f0eeeda…, mapLoader…,
кнопку «Сбросить выделенную область», но не содержит canvas.
Кнопка не доказывает готовность renderer или пиксельную видимость контура.
Исходный HTML сохранён локально в diagnostics/saved-errors/
2026-11-05_14-43-12_04310b75_aprelevka; в Git не переносится.
Прежний loader selector не учитывал эти class-признаки карты.

MapSelectionScript теперь читает видимые map loading markers в пределах карты.
MapSelectionState.Loading необязателен, default false для старых снимков.
Loading блокирует Active и передаётся в PageObservation.Loading.
Сборщик ограниченно ждёт явную загрузку карты: max(60, LoadWaitSeconds) секунд,
не расходуя попытки прокрутки на polling. Нет загрузки — прежние пределы остаются.
По таймауту выдаётся LOADING_INTERRUPTED с понятной причиной; данных вне
подтверждённой геометрии не принимает. Сравнение фильтров/геометрии не ослаблено.

## Файлы

- LocalCollection/DomSourcePage.cs, Models.cs, QueueRunner.cs.
- tests/LandErp.ParserSpike.Tests/MapSelectionTests.cs: загрузка дольше бюджета
  итераций с последующим успешным сбором; loading не разрешает cached geometry.
- Gate, ACTIVE_TASK, этот отчёт.
- Локальный offline JS runner artifacts/parser-map-loading/Test-MapLoading.cjs:
  реальный MapSelectionScript, видимый/скрытый loader, missing surface/reset/map,
  document readiness и признаки сохранённого HTML. Без браузера и сети.

## Проверки и установка

dotnet build tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj
с -c Release --no-restore -m:1 -p:UseSharedCompilation=false: 0 warnings/errors.
Test-Acceptance.ps1 в artifacts/parser-map-loading запускает прежние адресные
Parser/contract проверки и новые MapSelection tests; TRX в подпапке tests.
JS: 9/9 Passed. Первый acceptance обнаружил расширение polling на отсутствие
геометрии; исправлено ограничением исключения из бюджета только для Loading.
Итоговый acceptance: 187/187 Passed, 0 skipped, 44 секунды.

Release Desktop собирается в artifacts/parser-map-loading/local-update/ready.
Updater использует проверку процесса, сравнение собственных DLL с test output,
backup заменяемых файлов и hash protection данных/настроек/outbox/диагностики.
Рабочая папка artifacts/LandErp-Parser. Автозапуск не выполняется.
Установка завершена: 155 hashes verified, заменены 4 файла с backup;
128 защищённых файлов без изменений. verification.json в local-update.
Backup: backup-0492f8d86b9e4de7a07f60b8adb70fbb в local-update.
git diff --check затронутых tracked файлов: Passed.

## Ограничения

Это исправление наблюдаемой загрузки карты; оно не доказывает успешную загрузку
Avito в следующем live запуске. Если renderer не загрузится за отведённое время,
сбор безопасно остановится. Следующий шаг: один ручной запуск Апрелевки владельцем.
Server/schema/пакеты/Git publication не изменялись.
