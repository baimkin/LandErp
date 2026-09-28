# UX/UI package 02 — отчёт R1–R12

Дата: 27.09.2026. Реализация и адресные проверки завершены. Визуальная приёмка владельцем остаётся отдельным шагом.

## Рабочее состояние

- Рабочая копия: `C:/Users/user/.codex/visualizations/2026/09/27/01a0e349-b6ae-7843-abe9-4761aeb05c2f/ux02`.
- Ветка: `codex/ux-repair-package-02`; базовый HEAD `656df0e3a870c4088a6c450078908e9976d5f358`.
- Изменения не закоммичены. Push, merge, rebase, reset и изменения main не выполнялись.
- Семь файлов исходного hotfix перенесены с проверкой SHA256 перед работой. Исправления JS сохранены и расширены защитой сохранения. Исходная c346 по-прежнему содержит те же семь незакоммиченных файлов; её код не изменялся этой реализацией.
- Обновлён локальный тестовый сервер: [https://localhost:7240](https://localhost:7240). Он запущен из этой копии, PID после исправлений приёмки 28648. Проверены HTTP 200 для live, ready, страницы входа и общего обработчика полей.
- Используются прежняя локальная БД и прежнее файловое хранилище. Worker до обновления не был запущен; новый Worker собран, но отдельный планировщик в этой сессии не запускался.

## Что изменено и подтверждено

| Блок | Реализация | Фактическая проверка |
|---|---|---|
| R1 | Исправлены typed JS/stream-возвраты; кнопки не отправляют внешнюю форму; синхронная защита подготовки общения; ошибки чтения не обрывают circuit. Повтор загрузки использует тот же ID и проверяет содержимое/владельца. Частичный успех общения не создаёт повторное общение. | Chrome: заметка + файл + вставленная картинка; общение + задача + файл; повторное открытие. Нет page errors и beforeunload-диалогов. Node: ошибка/повтор, paste, deferred upload. PostgreSQL: retry/lost acknowledgement вложения, replay общения. |
| R2 | Общение показывает свои материалы и ссылку на задачу в переговорах и ходе работы; задача показывает исходное общение. Идентификаторы сохранены явно. Файлы открываются через штатный защищённый endpoint. | Chrome: исходный файл, выполненная задача, переход по ссылке из ленты. PostgreSQL: согласованное чтение карточки, панели и истории, чужие вложения/доступ отвергаются. |
| R3 | Компактные задачи, отдельный просмотр, меню действий, активные/выполненные, необязательный результат с материалами, автор/время завершения, ссылка из истории. Описание не заменяется результатом. Московская семантика date-only сохранена. | Chrome: завершение с текстом/файлом и повторное чтение. PostgreSQL: без отчёта, чужие файлы, replay, audit, сроки, видимость, закрытый объект и upgrade старых задач. |
| R4 | Общий обработчик HHmm → HH:mm; замена всех обнаруженных time/datetime-local полей, разделённая дата/время, ошибки неправильного времени. Четыре цифры форматируются до события Blazor. | Chrome: 0930 → 09:30; 9 вариантов TimeEntry; поиск не оставил native time/datetime-local в компонентах. Полная ручная матрица редактирования каждого поля не выполнялась. |
| R5 | Общий NumberInput и округление Money: пробелы, вставка валюты, пустое nullable-поле, дроби измерений; денежное отображение до рубля AwayFromZero. Значение БД не округляется при чтении/blur. | Chrome: 14 589 653,5 ₽ → 14 589 654; очистка остаётся пустой; 13,1 сохраняется; после изменения заметок/задач/проверки цена БД по-прежнему 14589653.5. |
| R6 | Первичны записи проверок; общие заметки свёрнуты. Видимый выбор шаблона или своего названия, необязательные ответственный и срок; старые значения сохраняются. | Chrome: готовый результат «В порядке» без назначения/срока; скриншот. Четыре целевых CaseCheckEntryTests: готовые результаты, legacy-поля, права, блокеры, конкуренция, материалы. |
| R7 | Общий CaseActivityFeed для переговоров, хода работы, полной вкладки истории объекта и истории входящего: дата слева, содержание и материалы, метаданные справа. Изменения цены/задачи раскрываются в «Подробнее». Исправлена подпись участия в медиане. | Пять CaseFeedTests; браузерное чтение ленты и реальная ссылка на выполненную задачу. Автор каталожного события не выдумывается, если его нет в контракте. |
| R8 | Читаемые ответы, смысловые группы в двух колонках с переходом в одну; локальная подпись отклонения и отступы. Полная карточка и просмотр в очереди обновлены. Оценка проблемы следует NormalAnswer, а не false. | Chrome: true → Да, false → Нет, пусто → Не указано; из двух false проблемен только отличающийся от нормы. Проверен скриншот полной карточки. Боковой просмотр проверен в дополнительном сценарии ниже. |
| R9 | Рабочая цена и торг слева, рыночные показатели справа; переход в переговоры, последовательное расположение на узком экране. Формулы рынка не изменены. | Реальные screenshots карточки на 1920 и 1024 px просмотрены; суммы без копеек. Многогрупповой рынок отдельно в браузере не проверялся. |
| R10 | Компактные пары фильтров, «В медиане» как условие отбора, окно сохранения фильтра, соседние действия. Короткое уведомление массового действия; одиночный успех показывает переключатель. ExcludedIds сохраняется в общей серверной выборке. | Chrome: числа, очистка, сохранение фильтра; PostgreSQL: all-results-minus-exclusions, preview/apply и новый предикат участия в медиане. |
| R11 | «Взять в работу» сразу создаёт/возвращает объект текущего сотрудника, закрывает панель, остаётся во входящих, показывает ссылку «Открыть». Связь с существующим объектом и передача сотруднику остаются отдельными действиями. | Chrome: один клик, панель закрыта, URL остаётся /incoming, ссылка доступна, в БД ровно один объект. Серверная идемпотентность и права прежнего действия сохранены. |
| R12 | Две независимые галереи; конкретные доказательные пары с листанием, честное состояние для старых записей. Exact означает равенство SHA256 содержимого; сходство pHash подписано как похожие фотографии. Заголовок/причины/действия закреплены, прокручивается содержимое. | PostgreSQL: две пары, ориентация и прежний алгоритм оценки. Chrome: 2 фото против 1, начальная доказательная пара, независимое листание, длинный текст; действия действительно в viewport до/после прокрутки. Скриншоты просмотрены. |

## Хранение и локальная миграция

Согласованные дополнения: nullable result_document_json/completed_at/completed_by_employee_id/source_negotiation_id задачи; точные ID задачи/общения у новых событий timeline; photo_evidence_json кандидата; nullable content_sha256 отпечатка фото. Содержимое файлов остаётся в прежнем хранилище; ссылки результата проверяются на доступность и принадлежность объекту.

Применены только две новые миграции:

1. `20260927173146_UxRepairTaskResultsAndPhotoEvidence`.
2. `20260927180000_TaskCommunicationAuditLinks` — восстановление старых связей задача–общение только по точным ID атомарного аудита с проверкой организации и объекта.

База `landerp_local`: до и после 6 объектов, 10 задач, 8 записей общения, 3 вложения, 427 объявлений. Сброса/очистки не было. На старых выполненных задачах неизвестные время/автор/результат остаются неизвестными. Старые timeline-события без ID не сопоставляются по тексту/времени. Старые кандидаты без доказательной пары показывают это явно; массовая повторная загрузка фотографий не запускалась.

## Выполненные проверки

- Release build Foundation.Tests с транзитивными Server/Worker/Application/Infrastructure: 0 предупреждений, 0 ошибок.
- `node scripts/Test-CaseTextInput.mjs`: PASS (plain text, paste, image paste, removal, deferred upload, failure/retry, legacy, cancel).
- `dotnet ef migrations has-pending-model-changes --project src/LandErp.Infrastructure --startup-project src/LandErp.Infrastructure --configuration Release --no-build`: изменений модели после миграции нет.
- `git diff --check`: без ошибок.
- Всего 30 уникальных успешно исполненных адресных вариантов, не полный suite: 9 TimeEntry, 2 браузерных, 2 CommunicationAndIndependentTaskCommitOnceAndReplayNeverChangesTask, 5 CaseFeed, 4 CaseCheckEntry, 5 CaseTasks (IndependentTasksEditCompleteDeleteReplayAndAudit; ResultRejectsForeignCaseFilesAndAllowsCompletionWithoutReport; MoscowDeadlinesSlicesNearestAndOverviewCountObjectsVersusTasks; OrganizationVisibilityAssigneeAndClosedCaseGuards; MigrationPreservesLegacyValuesAndCommentsAndAssigneeFilterSeesNonNearestTask), 1 NotesReuseDossierVisibilityAndProtectedAttachments, 1 PhotoFingerprintsDriveDuplicatesAndSettingsApplyWithoutRestart, 1 AllResultsMinusExclusionsAcrossPagesUsesSamePreviewAndApplyCohort.
- Последние тестовые команды использовали `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-build --no-restore --filter '<адресный фильтр>' --logger 'console;verbosity=normal'`, с LANDERP_DOTNET и приватным локальным PostgreSQL admin connection. Браузеры/временные базы создавались и убирались штатной тестовой fixture.
- В процессе исправлены обнаруженные ошибки: нетранслируемый LINQ в чтении файлов панели; устаревший label входа в helper; CSS-высота окна сравнения. Неудачные промежуточные прогоны не включены в число успешных. Первый PostgreSQL-запуск в sandbox остановлен, а не признан успешным; повтор с разрешённым localhost прошёл.

## Скриншоты после исправлений

Файлы находятся в `C:/Users/user/.codex/visualizations/2026/09/27/01a0e349-b6ae-7843-abe9-4761aeb05c2f/ux02/artifacts/ux02`. Это синтетические тестовые записи; изображения для сравнения подставлены браузерным route, а не загружены из внешнего сервиса.

- Первоначальные fullPage-снимки карточки 1920/1024 заменены доказательствами обычного viewport в разделе исправлений приёмки: положение sticky/fixed-элементов в старых склейках не является основанием для оценки оболочки.
- [Результат задачи](../../../artifacts/ux02/task-result-1440.png).
- [Проверки](../../../artifacts/ux02/checks-1440.png), [осмотр](../../../artifacts/ux02/inspection-1440.png).
- [Фильтры](../../../artifacts/ux02/incoming-filters-1440.png).
- [Сравнение](../../../artifacts/ux02/duplicate-comparison-1440.png), [после прокрутки](../../../artifacts/ux02/duplicate-comparison-scrolled-1440.png).

## Границы подтверждения и ручной просмотр

Полный suite, все сочетания прав/размеров, нулевая галерея и переключение нескольких доказательных пар в браузере, реальный отказ Yandex Disk/обрыв сети во время интерактивного сохранения отдельно не прогонялись. Ошибка и retry проверялись на уровне JS и PostgreSQL/тестового файлового сервиса. Production не затрагивался. В локальную пользовательскую тестовую базу синтетические сценарии не добавлялись.

Для ручной приёмки открыть прежний адрес сервера, обновить страницу; проверить общение/задачу/файл и выполненный результат; ввод времени и денежных сумм; проверку и осмотр; фильтр, одиночную медиану, взятие в работу и сравнение дублей. Старые записи без ранее сохранённых метаданных не должны показывать выдуманные значения.

## Устранение замечаний технической приёмки — 27.09.2026

Основание: `C:/.Projects/LandErp/artifacts/reviews/UX02_TECHNICAL_ACCEPTANCE_2026-09-27.md`. Это продолжение UX02; U-02 не начат. Полная техническая приёмка координатором и визуальная приёмка владельцем этим отчётом не объявляются.

- **R7:** `BusinessTimeline.razor` использует общий `CaseActivityFeed`. Полная вкладка «История» показывает выровненные дату, содержание, тип/автора, раскрываемые изменения до → после, исполнителя/сроки и ссылки. Исходные TimelineItem/метаданные сохранены. Найден и исправлен переход по ссылке задачи с другой вкладки: просмотр доступен над историей; закрытие сохраняет вкладку. Компонент готов до обновления query-параметров, что исключает найденный обрыв при изменении состава подписчиков навигации.
- **R10:** описание выбранного фильтра, предупреждение/кнопка обновления и строка найденного количества помещены в контейнеры с внутренними отступами не менее 16 px. Повторный браузерный сценарий прошёл, снимок снят обычным viewport.
- **R9:** под рабочей ценой расположены три отдельные строки «подпись — значение»: продавец, предложение, согласовано. Устранено наследование двух колонок от общего price-grid. Проверены заполненные суммы 2 000 001 / 1 800 000 / 1 900 000 ₽ и настоящая группа, созданная через сервис/БД из двух объявлений: медиана/средняя 133 333 ₽, тест спроса 125 001 ₽. На 1920 рынок справа, на 1024 последовательное расположение. Переключение нескольких групп не проверялось.
- **R8 и редактор задачи:** дополнена фактическая группировка результата осмотра в панели очереди. Проверены четыре группы, «Да», «Нет», «Не указано» и ровно одно отклонение от NormalAnswer; отсутствие затопления не помечается проблемой. Открыт активный редактор задачи в панели, заполнены заголовок, дата и 0930 → 09:30; горизонтального переполнения редактора нет.
- **Шапка/меню:** viewport-снимки 1920 и 1024 до/после прокрутки заменяют прежние fullPage-склейки. На 1920 положение `.topbar` до/после прокрутки равно 0. Оболочка не менялась.
- **Настоящий zoom 125%:** отдельный чистый профиль Chrome с штатной настройкой zoom, без CSS zoom и без подмены deviceScaleFactor. Измерено devicePixelRatio 1.25, окно 1440×1000, CSS viewport 1139×724; headerTop 0, горизонтального переполнения документа нет. Схема настройки сверена с [исходником Chromium](https://chromium.googlesource.com/chromium/src/+/lkgr/chrome/browser/ui/zoom/chrome_zoom_level_prefs.cc). Снимок берётся непосредственно из видимой области Chrome, чтобы избежать обрезки Playwright при NoViewport и увеличении.

Проверки после замечаний: сборка Foundation.Tests Release — 0 предупреждений/ошибок; 5 CaseFeedTests и IncomingFiltersGalleryAndOneClickTake прошли; новый AcceptanceHistoryPricesDrawerAndNativeZoom прошёл целиком. Это 7 адресных вариантов, а не повтор полного suite или первоначальных 30. Промежуточные падения раскрыли дефект перехода; не засчитывались. Первоначальная настройка профиля не применила zoom и была исправлена до успешной проверки.

Команды: `dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore -v quiet`; адресные `dotnet test ... -c Release --no-build --no-restore --filter FullyQualifiedName~CaseFeedTests`, `...~IncomingFiltersGalleryAndOneClickTake`, `...~AcceptanceHistoryPricesDrawerAndNativeZoom`; `git diff --check`. Новые сценарии выполнялись на изолированной тестовой БД; новые миграции для замечаний не нужны. Локальный сервер перезапущен из UX02, прежние БД/файлы сохранены; live/ready, `/account/login` и `/js/entry-format.js` вернули 200. Worker не запускался.

Чистые доказательства (`artifacts/ux02/acceptance`):

- [1920 до прокрутки](../../../artifacts/ux02/acceptance/case-filled-1920-top.png), [1920 после прокрутки](../../../artifacts/ux02/acceptance/case-filled-1920-scrolled.png).
- [1024 сверху](../../../artifacts/ux02/acceptance/case-filled-1024-top.png), [1024 заполненные цены](../../../artifacts/ux02/acceptance/case-filled-1024-prices.png).
- [Полная история с раскрытыми изменениями](../../../artifacts/ux02/acceptance/case-history-1440.png).
- [Редактор задачи в панели](../../../artifacts/ux02/acceptance/task-editor-drawer-1440.png), [результат осмотра из панели](../../../artifacts/ux02/acceptance/inspection-drawer-1440.png).
- [Chrome 125%](../../../artifacts/ux02/acceptance/case-native-zoom125.png), [измерения viewport](../../../artifacts/ux02/acceptance/viewport-evidence.json).
- [Фильтры с отступами](../../../artifacts/ux02/incoming-filters-1440.png).

Дополнительно затронутые файлы относительно первоначальной сдачи: BusinessTimeline.razor; CaseWorkspace.razor и .css; CaseNextAction.razor; ProcurementQueueV2.razor; IncomingCatalogV2.razor; incoming-v2-workflow.css; UxRepair02IncomingBrowserTests.cs; новый UxRepair02AcceptanceBrowserTests.cs; ACTIVE_TASK.md и этот отчёт. Следующий шаг — повторный ограниченный просмотр координатором; ручной просмотр владельцем остаётся отдельным.

## Затронутые файлы

Основные группы: модели/контракты Catalog, Workflow, Procurement; две миграции и snapshot; ProcurementWorkspace/CaseTasks/QueueV2 read models; Catalog query/read/detector и PhotoFingerprintWorker; общие NumberInput/NumberFormat/LocalDateTimeInput/PhotoGallery; entry-format.js и case-notes.js; компоненты общения, задач, проверок, лент, осмотра, цены и входящих; целевые тесты. Полный список ниже относится к этому рабочему diff, включая исходный hotfix.

```text
 M docs/03-active/ACTIVE_TASK.md
 M scripts/Test-CaseTextInput.mjs
 M src/LandErp.Application/Modules/Catalog/Domain/CatalogModels.cs
 M src/LandErp.Application/Modules/Catalog/Public/IncomingCatalogReadContracts.cs
 M src/LandErp.Application/Modules/Procurement/Public/CaseTaskContracts.cs
 M src/LandErp.Application/Modules/Procurement/Public/ProcurementContracts.cs
 M src/LandErp.Application/Modules/Procurement/Public/ProcurementQueueV2ReadContracts.cs
 M src/LandErp.Application/Modules/Workflow/Domain/WorkModels.cs
 M src/LandErp.Infrastructure/Migrations/LandErpDbContextModelSnapshot.cs
 M src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogQuery.cs
 M src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogReadService.cs
 M src/LandErp.Infrastructure/Modules/Catalog/IncomingDuplicateDetector.cs
 M src/LandErp.Infrastructure/Modules/Collection/CollectionMappings.cs
 M src/LandErp.Infrastructure/Modules/Procurement/CaseTimelineCommunication.cs
 M src/LandErp.Infrastructure/Modules/Procurement/ProcurementCaseTasks.cs
 M src/LandErp.Infrastructure/Modules/Procurement/ProcurementMappings.cs
 M src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.Modals.cs
 M src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.Projection.cs
 M src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs
 M src/LandErp.Server/ClientAssets/case-notes.js
 M src/LandErp.Server/Components/App.razor
 M src/LandErp.Server/Components/Pages/Collectors.razor
 M src/LandErp.Server/Components/Pages/DuplicateSettings.razor
 M src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor
 M src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor
 M src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor.css
 M src/LandErp.Server/Components/Pages/SiteInspectionPage.razor
 M src/LandErp.Server/Components/Primitives/DemandTestPrice.razor
 M src/LandErp.Server/Components/Procurement/CaseActivityFeed.razor
 M src/LandErp.Server/Components/Procurement/CaseCheckEntry.razor
 M src/LandErp.Server/Components/Procurement/CaseChecks.razor
 M src/LandErp.Server/Components/Procurement/CaseCommunication.razor
 M src/LandErp.Server/Components/Procurement/CaseDecisionDialog.razor
 M src/LandErp.Server/Components/Procurement/CaseFeedFormat.cs
 M src/LandErp.Server/Components/Procurement/CaseNextAction.razor
 M src/LandErp.Server/Components/Procurement/CaseNextAction.razor.css
 M src/LandErp.Server/Components/Procurement/CaseRichNoteBlock.razor
 M src/LandErp.Server/Components/Procurement/CaseTextInput.razor
 M src/LandErp.Server/Components/Procurement/CaseWorkspace.razor
 M src/LandErp.Server/Components/Procurement/CaseWorkspace.razor.css
 M src/LandErp.Server/Components/Procurement/DecisionDialog.razor
 M src/LandErp.Server/Components/Procurement/ProcurementLabels.cs
 M src/LandErp.Server/wwwroot/css/incoming-v2-workflow.css
 M src/LandErp.Server/wwwroot/js/case-notes.js
 M src/LandErp.Worker/PhotoFingerprintWorker.cs
 M tests/LandErp.Foundation.Tests/CaseFeedTests.cs
 M tests/LandErp.Foundation.Tests/CaseRichNoteTests.cs
 M tests/LandErp.Foundation.Tests/CaseTasksTests.cs
 M tests/LandErp.Foundation.Tests/IncomingMonitoringTests.cs
 M tests/LandErp.Foundation.Tests/MedianParticipationTests.cs
 M tests/LandErp.Foundation.Tests/ProcurementUiScenario.cs
?? docs/03-active/UI_UX_REPAIR_PACKAGE_02.md
?? docs/03-active/reports/UX_REPAIR_PACKAGE_02_REPORT.md
?? src/LandErp.Infrastructure/Migrations/20260927173146_UxRepairTaskResultsAndPhotoEvidence.Designer.cs
?? src/LandErp.Infrastructure/Migrations/20260927173146_UxRepairTaskResultsAndPhotoEvidence.cs
?? src/LandErp.Infrastructure/Migrations/20260927180000_TaskCommunicationAuditLinks.cs
?? src/LandErp.Server/Components/Primitives/LocalDateTimeInput.razor
?? src/LandErp.Server/Components/Primitives/LocalDateTimeInput.razor.css
?? src/LandErp.Server/Components/Primitives/NumberFormat.cs
?? src/LandErp.Server/Components/Primitives/NumberInput.razor
?? src/LandErp.Server/Components/Primitives/PhotoGallery.razor
?? src/LandErp.Server/Components/Primitives/PhotoGallery.razor.css
?? src/LandErp.Server/Components/Procurement/TimeEntry.cs
?? src/LandErp.Server/wwwroot/js/entry-format.js
?? tests/LandErp.Foundation.Tests/TimeEntryTests.cs
?? tests/LandErp.Foundation.Tests/UxRepair02BrowserTests.cs
?? tests/LandErp.Foundation.Tests/UxRepair02IncomingBrowserTests.cs
```
