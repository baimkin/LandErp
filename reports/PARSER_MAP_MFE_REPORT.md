# PARSER-MAP-MFE — 16.11.2026

Иславское job a8ccb22d-1513-4e36-9021-e1569c47b944 получило полигон,
но остановилось с AREA_SELECTION_UNCONFIRMED. В сохранённом HTML есть
map-view-mfe, ymaps3--tile-layer с img и кнопка сброса области.
Полноразмерного SVG/canvas рендерера у этой разновидности карты может не быть.

В MapSelectionScript добавлены map-view-mfe и видимые загруженные тайлы
внутри Avito map containers. Изображение должно быть complete, иметь naturalWidth
и пересекать viewport. Родитель карты должен иметь достаточный размер.
Прежние map-view/map-full и canvas/svg сохранены. GeoJSON, filters, loading,
активное выделение и проверка координат продолжают проверяться отдельно.
Наличие поверхности само по себе не даёт разрешения принять карточку.

Затронуты DomSourcePage.cs, scripts/Test-ParserMapSelection.cjs и
tests/LandErp.ParserSpike.Tests/Fixtures/avito-map-mfe-shape.json.
Fixture содержит только признаки разметки из HTML, без исходных адресов/данных.
В тесте реальные выражения production JS исполняются через node:vm
с управляемыми размерами, видимостью и готовностью изображений.
Это проверка DOM-логики с моделью layout, а не визуальный тест браузера.

Проверки:
- node scripts/Test-ParserMapSelection.cjs: 17/17 Passed.
- прежний Test-MapLoading.cjs: 9/9 Passed.
- прежний Test-PaginationScript.cjs: 11/11 Passed.
- Release build tests и Desktop с --no-restore -m:1
  -p:UseSharedCompilation=false: 0 warnings/errors.
- Адресный набор artifacts/parser-map-mfe/Test-Acceptance.ps1.
  Результат 203/203 Passed, 44 секунды. TRX в artifacts/parser-map-mfe/tests.

Рабочая папка artifacts/LandErp-Parser. Staging/update evidence в
artifacts/parser-map-mfe/local-update, включая backup и verification.json.
Live источники не запускались. После установки владелец проверяет Иславское
через обычный Parser. Поддержка tile renderer требует этого ручного контроля.
Установка завершена. Проверены 155 файлов, заменены 4 с резервной копией
backup-b79af2132b69476a980235937fea03b6. Все 235 защищённых файлов сохранили
хеши. Parser автоматически не запускался.

## Рабочая проверка в диагностике — 17.11.2026

Снимок 10:22 подтвердил геометрию и визуальные признаки, но dom-state.json
использовал упрощённую проверку видимости. Добавлен selection-state.json,
исполняющий тот же MapSelectionScript с diagnostic=true.
Флаги MapVisible/SurfaceVisible/ResetVisible/Loading вычисляются общим путём.
Описание включает размеры/расположение, код блокировки и родительский уровень,
tag и ограниченный map-marker. Исходный текст, URL, id и классы не записываются.
Также видны готовность тайлов, disabled reset и индикаторы загрузки.
Массивы диагностических элементов ограничены. Ошибка снимка фиксируется
отдельно с префиксом SELECTION, остальные файлы сохраняются независимо.
Правила видимости/сбора не менялись.

Файлы: DomSourcePage.cs, MapDiagnosticTests.cs, Test-ParserMapSelection.cjs.
Тесты проверяют одинаковые флаги в обоих режимах, причины display/visibility/
opacity/offscreen, disabled/missing reset и фактическую запись production
скрипта в selection-state.json через mock IPage.
204/204 адресных .NET тестов Passed, 44 секунды; 28/28 JS сценариев и
9/9 прежних loading сценариев Passed. Две Release сборки без warnings/errors.
git diff --check прошёл. Рабочая сборка обновлена: 155 hashes verified,
4 файла с backup-be2921bbbe774446b594a836b76f9d3d в
artifacts/parser-selection-details/local-update. Хеши 248 защищённых файлов
совпали. verification.json находится там же. Владелец сохраняет следующий
снимок Иславского, live запуск агентом не выполнялся.

## Автоматический снимок ошибки — 17.11.2026

По запросу владельца в LastErrorDetails добавлено необязательное MapSelection.
LastRead хранит флаги последней рабочей проверки с временем и безопасным URL.
AtError содержит повторный запуск того же скрипта с diagnostic=true при
завершении обычного сбора с ошибкой. CaptureError содержит только тип исключения,
если повторная проверка невозможна. Кэш LastRead сохраняется при закрытии
страницы и сбрасывается на Open новой ссылки.

ISourcePage имеет метод с default null, поэтому старые fake/source adapters
продолжают работать. Для Cian MapSelection отсутствует. Снимок публикуется
в существующем last-error.json вместе с HTML под прежней защитой sequence.
Сбор флагов имеет отдельный deadline 3 секунды после HTML, внутреннее ожидание
страницы 2 секунды. Отсутствие HTML не мешает записи флагов. Сбой новой
диагностики не изменяет результат и observations.

Файлы: DomSourcePage.cs, LastErrorSnapshot.cs, Models.cs,
LastErrorSnapshotTests.cs, MapBatchesPaginationTests.cs, Gate/ACTIVE_TASK.
Проверки: 207/207 .NET tests Passed, 44 секунды. 28/28 JS scenarios Passed.
Обе Release сборки без предупреждений/ошибок. git diff --check прошёл.
Тесты покрывают публикацию при неуспешном HTML, различающиеся LastRead/AtError,
ошибку повторного чтения, сохранение кэша закрытой страницы и защиту от
позднего старого снимка. TRX в artifacts/parser-error-selection/tests.
Staging/update script в artifacts/parser-error-selection/local-update.
Владелец закрыл Parser. Рабочая сборка обновлена, 155 hashes verified,
4 файла заменены с backup-c462dcf96648498dbf14045c78fa1681 в local-update.
Хеши 256 защищённых файлов совпали, результат в verification.json.
Далее обычный сбор Иславского, без отдельного диагностического режима.

## Interactive вместо ожидания всех ресурсов — 17.11.2026

В last-error 0b06a13d-b737-4c17-84f1-e5c8b8a2ac2a
(Клязьминское вдхр., 11:04 МСК) получен контур, SurfaceVisible=true,
ResetVisible=true, Loading=false. MapVisible=false объясняется исключительно
readyState=interactive. В описании DOM оба map containers без отказа видимости.
Владелец разрешил принять interactive при остальных готовых признаках.

MapSelectionScript допускает interactive и complete. loading и неизвестные
состояния документа продолжают давать MapVisible=false. Проверки полигонов,
координат, источника, фильтров, поверхности, активного выделения и индикаторов
загрузки сохранены. Общий скрипт применяется в обычном сборе и диагностике.

Затронуты DomSourcePage.cs и scripts/Test-ParserMapSelection.cjs.
JS: 36/36, включая готовую карту interactive и семь отказных случаев с
interactive (скрытая карта, отсутствие поверхности/reset, disabled,
loading, недогруженный или повреждённый tile). Release build tests и Desktop
прошли без warnings/errors. Staging и updater находятся в
artifacts/parser-map-interactive/local-update. Проверка git diff --check прошла.
Live запуск агентом не выполнялся.
Адресный .NET набор: 207/207 Passed, 44 секунды. TRX в
artifacts/parser-map-interactive/tests. На момент завершения проверок
PID 36880 работал, установка ожидает закрытия окна пользователем.
Затем владелец закрыл Parser. Рабочая сборка обновлена, сверены 155 файлов,
заменены 4 с backup-86154ce5952940d89a3ea7b713153ac8 в local-update.
Хеши 259 защищённых файлов совпали, результат в verification.json.
