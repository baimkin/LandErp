# PARSER-MAP-DIAGNOSTICS — 03.11.2026

Подготовлен изолированный запуск для сравнения Avito с видимым контуром и
без контура. Это подготовка evidence, а не исправление определения контура.
Обычный сбор пока сохраняет прежнее поведение; для проверок использовать
только ярлык «Диагностика карты.lnk» с `--map-diagnostics`.

## Изменения

- `MapDiagnosticCapture.cs`: пассивные first-party HTTPS ответы document/fetch/xhr,
  безопасная схема JSON, выбранная геометрия и decoded drawAreaBase64.
  Значения неизвестных строк, авторизационные/личные ветки, HTTP headers и raw
  тела не сохраняются; URL запросов без query/credentials/opaque path components.
  160 ответов, 3 pending, окно 3 минуты, JSON до 4 МиБ (проверяется Content-Length
  и фактический размер), journal до 16 Ми символов, bounded flush. Браузер может
  уже буферизовать тело неизвестного размера; ограничение не заявляется как
  гарантия максимальной памяти самого Chromium. Skipped отражаются в capture.json.
- `DomSourcePage.cs`, `Models.cs`: observer до навигации, отдельные методы
  диагностики; DOM state/размеры canvas и SVG/path/stroke, очищенный HTML,
  screenshot, ошибки доступности. Даже закрытая страница сохраняет полученное.
  Не вызываются Read/scroll/zoom/pagination, обычный map collector отключён.
- `WorkspaceController.cs`: отдельный запуск игнорирует persisted AutoOn, не
  восстанавливает jobs, не исполняет Tick/claim/delivery, не создаёт outbox.
  Server workspace читается напрямую без регистрации/создания coordinator.
  Каждое открытие — уникальная папка `local-data/diagnostics/map-captures`.
  search.json связывает capture с локальным/серверным ID и SHA-256 URL.
- `WorkspaceWindow.xaml/.cs`: две кнопки только в диагностическом запуске,
  вставка URL как альтернатива выбору поиска; auto/start disabled, явный заголовок.
  После сохранения открывается папка. Обычный запуск не меняется.
- `MapDiagnosticTests.cs`, `WorkspaceUiTests.cs`: приватность/ограничение schema,
  отсутствие продвижения viewport в selected geometry, startup AutoOn, observer
  before navigation, отсутствие jobs/listings/outbox, отдельные папки, navigation
  timeout, closed page, сохранение network и Unknown, UI buttons/disabled collection.
- README, Gate, scoped запись ACTIVE_TASK. Параллельные media/server изменения
  сохранены; commits/merge/push не выполнялись.

## Проверки

`dotnet build tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-restore`
— Passed, 0 warnings/errors.

`artifacts/parser-map-diagnostics/Test-Acceptance.ps1` — финальные **161/161 Passed**.
Выборка явно ограничена небраузерными Parser/contract/WPF UI проверками;
Chromium/маркетплейсы не запускались. TRX:
`artifacts/parser-map-diagnostics/tests/map-diagnostic-acceptance.trx`.

Release staging: `dotnet build src/LandErp.ParserSpike.Desktop/LandErp.ParserSpike.Desktop.csproj -c Release --no-restore -o artifacts/parser-map-diagnostics/local-update/ready`.
Перед установкой повторная normal Release build и hash comparison трёх own DLL:
тестируемые assemblies и staging совпадают. `git diff --check` затронутых
tracked Parser/docs файлов прошёл (только предупреждение LF/CRLF).

Рабочая папка `artifacts/LandErp-Parser` обновлена: проверены SHA-256 всех
156 файлов, обновлены 5 (4 прежних сохранены в backup, 1 новый ярлык).
39 защищённых файлов без изменений. Evidence:
`artifacts/parser-map-diagnostics/local-update/verification.json`.
Backup: `artifacts/parser-map-diagnostics/local-update/backup-7d6fc6c3c10142e6b4e8cee0c5a5c458`.
Ярлык проверен: target — рабочий exe, аргумент `--map-diagnostics`.
Parser автоматически не запускался.

## Границы и дальнейшая проверка владельцем

RenderedOutline всегда Unknown: наличие geometry/canvas/reset не является
решением разрешить импорт. Существующие эвристики сбора не расширялись.
Владелец запускает диагностический ярлык, открывает и сохраняет одну ссылку
с видимым контуром и другую без него, не меняя масштаб. Артефакты не затираются.
По двум материалам можно реализовать проверяемое разрешение сбора; пока
проверка live source/полноты и actual contour detection не выполнена.
