# PARSER-MAP-SELECTION — 03.11.2026

По прямому запросу владельца доработана защита сбора Avito на карте.
Ветка codex/inspection-file-improvements, накопленные/параллельные изменения
сохранены. Server/Cian/обычный список без зоны/контракт доставки не менялись.
Для списка с нарисованной зоной общий guard также перестал разрешать новые
данные по одному старому эталону; автоматического переключения list/map нет.

## Итоговое поведение

- Любая распознанная карта, включая ссылку без drawId, проходит SearchAreaGuard.
  Первый снимок и финальный контроль не могут обойти guard через отсутствие ID.
- Допуск требует текущую валидную геометрию, неизменные фильтры, завершённую
  загрузку, видимую карту/поверхность и доступную видимую кнопку сброса области.
  Unknown state блокирует новые observations; drawId самостоятельно не разрешает
  и не запрещает сбор. Эталон остаётся неизменным.
- Кэшированный эталон без текущей геометрии больше не разрешает новые точки.
  Изменённая/отсутствующая геометрия или неподтверждённое выделение подавляют
  Listings и Changes; такие карточки сохраняются только в quarantine diagnostics.
- После подтверждения применяется point-in-polygon, включая holes/boundaries.
  Точки без координат не импортируются; вне области исключаются.
- При сбросе до старта — ноль импортированных карточек и ноль прокруток.
  При сбросе после начала — Partial и сохранение ранее проверенных observations,
  без добавления нового неподтверждённого снимка. Server получает полезную часть
  по прежнему classifier/outbox протоколу; итог не становится полной выдачей.
- Для загрузки/состояния выделения сохранено ограниченное ожидание; карта не
  прокручивается до допуска. Причина результата объясняет отсутствие текущего
  контура/активного выделения, а не требует ручного подтверждения старых ссылок.

## Файлы

`Models.cs` (optional MapSelectionState), `DomSourcePage.cs` (read-only DOM state),
`SearchAreaGuard.cs`, `QueueRunner.cs`, `MapSelectionTests.cs`,
`SavedSearchAreaTests.cs`, `AvitoMapTests.cs`, `AvitoSearchIdentityTests.cs`,
test csproj, две `Fixtures/avito-selection-*.json`, README, ACTIVE_TASK/Gate/отчёт.
Старые fixtures обычного map runner явно сообщают Active selection; несколько
прежних ожиданий приёма по одному кэшированному эталону заменены на отказ в
соответствии с новым запросом. Проверки ранее подтверждённых данных сохранены.

Fixtures получены из двух сохранённых диагностик 19:17 и 19:18 МСК: только
числовой полигон (56 точек) из decoded drawAreaBase64 либо отсутствие поля.
Никаких cookies/auth/raw HTTP/личных данных в fixtures нет.

## Фактически выполненные проверки

- `dotnet build tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false`:
  Passed, 0 warnings/errors. Shared compilation выключена после временного
  отказа записи compiler output; правок permissions/остановки чужих процессов нет.
- `artifacts/parser-map-selection/Test-Acceptance.ps1`: **170/170 Passed**.
  Evidence: `artifacts/parser-map-selection/tests/selection-acceptance.trx`.
  Включены geometry, state unknown/hidden/missing, cached reference, old links,
  filters, no drawId, reset before/during scroll, Partial delivery, coverage,
  durable contract/outbox и прежние адресные Parser/WPF checks.
- `node artifacts/parser-map-selection/Test-SelectionScript.cjs`: **10/10**,
  исполняется настоящий MapSelectionScript в моделируемом DOM: normal/title-only,
  loading, hidden map/ancestor/reset, missing surface, disabled reset, offscreen,
  absent map. Browser/network не используются.
- Release staging `dotnet build src/LandErp.ParserSpike.Desktop/LandErp.ParserSpike.Desktop.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -o artifacts/parser-map-selection/local-update/ready`:
  Passed, 0 warnings/errors. Три own DLL совпали SHA-256 с проверенной сборкой.
- `git diff --check` адресных tracked файлов: Passed, только LF/CRLF warnings.

## Рабочая установка

Владелец подтвердил закрытие Parser. Установлены 4 изменённых программных файла,
все 4 сохранены в backup. SHA-256 проверены для всех 155 staged программных
файлов; 51 защищённый файл (БД/outbox/настройки/диагностика/эталоны) без изменений.
Рабочая папка: `artifacts/LandErp-Parser`; диагностический ярлык сохраняется.
Evidence: `artifacts/parser-map-selection/local-update/verification.json`.
Backup: `artifacts/parser-map-selection/local-update/backup-c813e7882a3e44adbeb23dd97973f5f0`.
Parser автоматически не запускался.

## Ограничения

Проверяется согласованное состояние источника и активного выделения UI.
Canvas с валидной геометрией и активной кнопкой может теоретически не нарисовать
линию: видимость отдельных пикселей контура не доказана этими проверками.
При скрытой карте/поверхности, отсутствующей кнопке, геометрии или неизвестном
состоянии сбор блокируется. Проверка не объявляется универсальным доказательством
отрисовки canvas. Реальный браузер/живой Avito после изменения не запускались.
Следующий шаг владельца: один контрольный обычный запуск ссылки с контуром,
проверка прокрутки/координат/итога; затем контроль ссылки со сброшенной областью.
Git publication, production, новые пакеты и следующие Gates не выполнялись.
